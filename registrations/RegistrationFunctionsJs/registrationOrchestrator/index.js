const df = require("durable-functions");

const REAL_LINK_EXPIRY_MS = (Number(process.env.CONFIRMATION_EXPIRY_MINUTES) || 15) * 60 * 1000;

module.exports = df.orchestrator(function* (context) {
    const input = context.df.getInput();
    const { contactId, email, isRealEmail, registration } = input;
    const instanceId = context.df.instanceId;

    let attempt = 0;
    let confirmed = false;

    while (!confirmed) {
        attempt++;
        const sendResult = yield context.df.callActivity("sendConfirmationEmailActivity", {
            contactId, email, isRealEmail, instanceId, attempt
        });

        if (isRealEmail) {
            const expireAt = new Date(context.df.currentUtcDateTime.getTime() + REAL_LINK_EXPIRY_MS);
            const confirmTask = context.df.waitForExternalEvent("EmailConfirmed");
            const timeoutTask = context.df.createTimer(expireAt);

            const winner = yield context.df.Task.any([confirmTask, timeoutTask]);

            if (winner === confirmTask) {
                timeoutTask.cancel();
                confirmed = true;
                continue;
            }

            // The link has expired. Keep the instance alive - with no timer running - waiting
            // for either a late click on the same link or a resend request. This is the state
            // worth pointing at in Storage Explorer during the demo: "Running", no pending
            // timer, just parked on two external events.
            const lateConfirmTask = context.df.waitForExternalEvent("EmailConfirmed");
            const resendTask = context.df.waitForExternalEvent("ResendRequested");
            const waiting = yield context.df.Task.any([lateConfirmTask, resendTask]);

            if (waiting === lateConfirmTask) {
                confirmed = true;
            }
            // else ResendRequested: loop back around and send a fresh link.
        } else {
            // Fake (Bogus-generated) address - nothing is actually emailed. Auto-simulate the
            // click after a short randomized delay so bulk ingestion still produces
            // registrations, and so the delay window shows up as a live, running orchestration
            // instance during the demo.
            const fireAt = new Date(context.df.currentUtcDateTime.getTime() + sendResult.simulatedDelaySeconds * 1000);
            yield context.df.createTimer(fireAt);
            confirmed = true;
        }
    }

    const created = yield context.df.callActivity("createRegistrationActivity", { contactId, registration });
    return { status: "registered", contactId, registrationId: created.registrationId };
});
