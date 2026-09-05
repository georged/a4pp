const crypto = require("crypto");

let cachedToken = null;
let cachedTokenExpiresAt = 0;

async function getAccessToken(env) {
    if (cachedToken && Date.now() < cachedTokenExpiresAt) {
        return cachedToken;
    }
    const tokenRes = await fetch(`https://login.microsoftonline.com/${process.env.DATAVERSE_TENANT_ID}/oauth2/v2.0/token`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
            grant_type: "client_credentials",
            client_id: process.env.DATAVERSE_CLIENT_ID,
            client_secret: process.env.DATAVERSE_CLIENT_SECRET,
            scope: `${env}/.default`
        })
    });
    const { access_token, expires_in } = await tokenRes.json();
    cachedToken = access_token;
    cachedTokenExpiresAt = Date.now() + (expires_in - 60) * 1000;
    return cachedToken;
}

module.exports = async function (context, messages) {
    const batch = messages.map((m) => (typeof m === "string" ? JSON.parse(m) : m));
    context.log(`Processing a batch of ${batch.length} registrations`);

    const env = process.env.DATAVERSE_ENVIRONMENT_URL;
    const accessToken = await getAccessToken(env);

    // One changeset containing a deep insert (contact + linked tot_registration) per message,
    // sent to Dataverse's OData $batch endpoint as a single HTTP request.
    const batchBoundary = `batch_${crypto.randomUUID()}`;
    const changesetBoundary = `changeset_${crypto.randomUUID()}`;

    const changesetParts = batch.map((data, i) => {
        const body = {
            firstname: data.customer.firstName,
            lastname: data.customer.lastName,
            emailaddress1: data.customer.email,
            tot_registration_contact: [
                {
                    tot_externalid: data.registrationId,
                    tot_campaigncode: data.campaignCode,
                    tot_eventid: data.eventId,
                    tot_quantity: data.quantity,
                    tot_sourcesystem: data.sourceSystem,
                    tot_submittedon: data.eventTime,
                    tot_tickettype: data.ticketType,
                    tot_totalamount: data.totalAmount
                }
            ]
        };
        return `--${changesetBoundary}\r\n` +
            `Content-Type: application/http\r\n` +
            `Content-Transfer-Encoding: binary\r\n` +
            `Content-ID: ${i + 1}\r\n\r\n` +
            `POST ${env}/api/data/v9.2/contacts HTTP/1.1\r\n` +
            `Content-Type: application/json\r\n\r\n` +
            `${JSON.stringify(body)}\r\n`;
    }).join("");

    const batchBody = `--${batchBoundary}\r\n` +
        `Content-Type: multipart/mixed;boundary=${changesetBoundary}\r\n\r\n` +
        changesetParts +
        `--${changesetBoundary}--\r\n` +
        `--${batchBoundary}--\r\n`;

    const batchHeaders = {
        "Authorization": `Bearer ${accessToken}`,
        "Content-Type": `multipart/mixed;boundary=${batchBoundary}`,
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
    };

    let res = await fetch(`${env}/api/data/v9.2/$batch`, { method: "POST", headers: batchHeaders, body: batchBody });
    while (res.status === 429) {
        const waitSeconds = Number(res.headers.get("Retry-After")) || 5;
        context.log(`Throttled by Dataverse, retrying batch in ${waitSeconds}s`);
        await new Promise((resolve) => setTimeout(resolve, waitSeconds * 1000));
        res = await fetch(`${env}/api/data/v9.2/$batch`, { method: "POST", headers: batchHeaders, body: batchBody });
    }

    context.log(`Batch of ${batch.length} registrations completed with status ${res.status}`);
};
