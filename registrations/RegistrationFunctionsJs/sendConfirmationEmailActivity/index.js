const { dataverseFetch } = require("../shared/dataverseClient");

const MIN_DELAY_SECONDS = Number(process.env.SIMULATED_CONFIRM_DELAY_MIN_SECONDS) || 15;
const MAX_DELAY_SECONDS = Number(process.env.SIMULATED_CONFIRM_DELAY_MAX_SECONDS) || 90;

module.exports = async function (context, input) {
    const { email, isRealEmail, instanceId, attempt } = input;

    if (!isRealEmail) {
        const simulatedDelaySeconds = MIN_DELAY_SECONDS + Math.floor(Math.random() * (MAX_DELAY_SECONDS - MIN_DELAY_SECONDS + 1));
        context.log(`Simulating confirmation click for fake address ${email} in ${simulatedDelaySeconds}s (instance ${instanceId})`);
        return { wasReal: false, simulatedDelaySeconds };
    }

    const expiryMinutes = process.env.CONFIRMATION_EXPIRY_MINUTES || 15;
    const confirmLink = `${process.env.FUNCTION_APP_BASE_URL}/api/confirmRegistration?id=${instanceId}`;
    const subject = attempt > 1 ? "Your registration link (resent)" : "Confirm your registration";
    const body = `Please confirm your registration by clicking the link below:<br/><br/>` +
        `<a href="${confirmLink}">${confirmLink}</a><br/><br/>` +
        `This link expires in ${expiryMinutes} minutes.`;

    // CreateAndSendNewEmail parameter shapes confirmed against the official reference
    // (https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/createandsendnewemail):
    // Subject/Body/From/To are plain strings, not activityparty objects, and From must resolve
    // to an existing Dataverse user or queue mailbox.
    const res = await dataverseFetch("CreateAndSendNewEmail", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            Subject: subject,
            Body: body,
            From: process.env.CONFIRMATION_EMAIL_FROM,
            To: email
        })
    });

    if (!res.ok) {
        const errText = await res.text();
        throw new Error(`CreateAndSendNewEmail failed with ${res.status}: ${errText}`);
    }

    context.log(`Sent confirmation email to ${email} (instance ${instanceId}, attempt ${attempt})`);
    return { wasReal: true };
};
