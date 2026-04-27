module.exports = async function (context, req) {
    context.log("HTTP request received. Queuing body.");

    const body = req.body;

    if (!body) {
        context.res = {
            status: 400,
            body: "Request body is required."
        };
        return;
    }

    context.bindings.queueMessage = body;

    context.res = {
        status: 202,
        body: "Message queued."
    };
};
