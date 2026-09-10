const df = require("durable-functions");

module.exports = async function (context, req) {
    const instanceId = req.query.id;
    if (!instanceId) {
        context.res = { status: 400, body: "Missing id." };
        return;
    }

    const client = df.getClient(context);
    const status = await client.getStatus(instanceId);
    if (!status || status.runtimeStatus !== "Running") {
        context.res = { status: 410, body: "This registration can no longer be resent." };
        return;
    }

    // raiseEvent JSON.stringifies its eventData for the request body; omitting it entirely
    // leaves the body undefined, which the Durable Task extension rejects as invalid JSON.
    await client.raiseEvent(instanceId, "ResendRequested", true);
    context.res = { status: 200, body: "A new confirmation link is on its way." };
};
