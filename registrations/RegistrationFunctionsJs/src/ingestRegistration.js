const { app, output } = require("@azure/functions");

const registrationsQueueOutput = output.serviceBusQueue({
  queueName: "registrations",
  connection: "ServiceBusConnection"
});

app.http("ingestRegistration", {
  methods: ["POST"],
  authLevel: "function",
  route: "ingest",
  extraOutputs: [registrationsQueueOutput],
  handler: async (request, context) => {
    const body = await request.text();
    context.extraOutputs.set(registrationsQueueOutput, body);
    context.log("Queued incoming HTTP payload.");

    return {
      status: 202,
      body: "Accepted."
    };
  }
});
