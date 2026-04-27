const { app } = require("@azure/functions");
const {
  upsertContact,
  registrationExists,
  createRegistration
} = require("./dataverseClient");

app.serviceBusQueue("processRegistration", {
  queueName: "registrations",
  connection: "ServiceBusConnection",
  handler: async (message, context) => {
    const messageText = typeof message === "string" ? message : JSON.stringify(message);
    const registration = JSON.parse(messageText);

    const contactId = await upsertContact(registration.customer);

    if (await registrationExists(registration.registrationId)) {
      context.log(`Registration ${registration.registrationId} already exists, skipping.`);
      return;
    }

    await createRegistration(registration, contactId);
    context.log(`Created registration ${registration.registrationId} for contact ${contactId}.`);
  }
});
