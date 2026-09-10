const crypto = require("crypto");
const df = require("durable-functions");
const { dataverseFetch } = require("../shared/dataverseClient");

function parseAllowlist() {
    return (process.env.REAL_DEMO_EMAILS || "")
        .split(",")
        .map((e) => e.trim().toLowerCase())
        .filter(Boolean);
}

function isAllowlistedEmail(email, allowlist) {
    const normalizedEmail = (email || "").trim().toLowerCase();

    return allowlist.some((entry) => {
        if (entry.startsWith("@")) {
            return normalizedEmail.endsWith(entry);
        }

        return normalizedEmail === entry;
    });
}

// Dataverse returns batch parts in request order for a plain (non-changeset) batch, so the
// part at index i corresponds to items[i]. Each part's own HTTP status line tells us whether
// that specific contact create succeeded, which continue-on-error makes independent of the
// others.
function parsePartStatuses(responseText, boundary) {
    return responseText
        .split(`--${boundary}`)
        .filter((part) => part.trim() && part.trim() !== "--")
        .map((part) => {
            const statusMatch = part.match(/HTTP\/1\.1 (\d+)/);
            return statusMatch ? Number(statusMatch[1]) : 0;
        });
}

module.exports = async function (context, messages) {
    const batch = messages.map((m) => (typeof m === "string" ? JSON.parse(m) : m));
    context.log(`Processing a batch of ${batch.length} registrations`);

    const allowlist = parseAllowlist();
    const items = batch.map((data) => ({
        contactId: crypto.randomUUID(),
        data,
        isRealEmail: isAllowlistedEmail(data.customer.email, allowlist)
    }));

    // Only the contact is written here. The tot_registration record is created later by
    // createRegistrationActivity, once the orchestration confirms the email address - deep
    // insert isn't used any more since contact and registration are no longer created together.
    const env = process.env.DATAVERSE_ENVIRONMENT_URL;
    const batchBoundary = `batch_${crypto.randomUUID()}`;

    const requestParts = items.map((item, i) => {
        const body = {
            contactid: item.contactId,
            firstname: item.data.customer.firstName,
            lastname: item.data.customer.lastName,
            emailaddress1: item.data.customer.email
        };
        return `--${batchBoundary}\r\n` +
            `Content-Type: application/http\r\n` +
            `Content-Transfer-Encoding: binary\r\n` +
            `Content-ID: ${i + 1}\r\n\r\n` +
            `POST ${env}/api/data/v9.2/contacts HTTP/1.1\r\n` +
            `Content-Type: application/json\r\n\r\n` +
            `${JSON.stringify(body)}\r\n`;
    }).join("");

    const batchBody = requestParts + `--${batchBoundary}--\r\n`;

    const res = await dataverseFetch("$batch", {
        method: "POST",
        headers: {
            "Content-Type": `multipart/mixed;boundary=${batchBoundary}`,
            "Prefer": "odata.continue-on-error"
        },
        body: batchBody
    });

    const responseText = await res.text();
    const contentType = res.headers.get("content-type") || "";
    const responseBoundary = (contentType.match(/boundary=([^;]+)/) || [])[1];
    const statuses = responseBoundary ? parsePartStatuses(responseText, responseBoundary) : [];

    context.log(`Batch of ${items.length} contacts completed with status ${res.status}`);

    const client = df.getClient(context);
    let started = 0;
    for (let i = 0; i < items.length; i++) {
        const item = items[i];
        const partStatus = statuses[i];
        if (!(partStatus >= 200 && partStatus < 300)) {
            context.log.error(`Contact create failed for registration ${item.data.registrationId} (status ${partStatus})`);
            continue;
        }

        await client.startNew("registrationOrchestrator", item.contactId, {
            contactId: item.contactId,
            email: item.data.customer.email,
            isRealEmail: item.isRealEmail,
            registration: {
                externalId: item.data.registrationId,
                campaignCode: item.data.campaignCode,
                eventId: item.data.eventId,
                quantity: item.data.quantity,
                sourceSystem: item.data.sourceSystem,
                submittedOn: item.data.eventTime,
                ticketType: item.data.ticketType,
                totalAmount: item.data.totalAmount
            }
        });
        started++;
    }

    context.log(`Started ${started} registration orchestrations`);
};
