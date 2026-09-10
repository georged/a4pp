const { dataverseFetch } = require("../shared/dataverseClient");

module.exports = async function (context, input) {
    const { contactId, registration } = input;

    const body = {
        tot_externalid: registration.externalId,
        tot_campaigncode: registration.campaignCode,
        tot_eventid: registration.eventId,
        tot_quantity: registration.quantity,
        tot_sourcesystem: registration.sourceSystem,
        tot_submittedon: registration.submittedOn,
        tot_tickettype: registration.ticketType,
        tot_totalamount: registration.totalAmount,
        "tot_Contact@odata.bind": `/contacts(${contactId})`
    };

    const res = await dataverseFetch("tot_registrations", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Prefer": "return=representation"
        },
        body: JSON.stringify(body)
    });

    if (!res.ok) {
        const errText = await res.text();
        throw new Error(`Registration create failed with ${res.status}: ${errText}`);
    }

    const created = await res.json();
    context.log(`Created registration ${created.tot_registrationid} for contact ${contactId}`);
    return { registrationId: created.tot_registrationid };
};
