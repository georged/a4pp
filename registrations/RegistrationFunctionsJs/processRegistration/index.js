module.exports = async function (context, message) {
    const data = typeof message === "string" ? JSON.parse(message) : message;
    context.log(`Processing registration ${data.registrationId} for ${data.customer.email}`);

    const env = process.env.DATAVERSE_ENVIRONMENT_URL;

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
    const { access_token } = await tokenRes.json();

    const dataverseHeaders = {
        "Authorization": `Bearer ${access_token}`,
        "Content-Type": "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
    };

    async function postToDataverse(entitySet, body) {
        let res = await fetch(`${env}/api/data/v9.2/${entitySet}`, { method: "POST", headers: dataverseHeaders, body: JSON.stringify(body) });
        while (res.status === 429) {
            const waitSeconds = Number(res.headers.get("Retry-After")) || 5;
            context.log(`Throttled by Dataverse creating ${entitySet}, retrying in ${waitSeconds}s`);
            await new Promise((resolve) => setTimeout(resolve, waitSeconds * 1000));
            res = await fetch(`${env}/api/data/v9.2/${entitySet}`, { method: "POST", headers: dataverseHeaders, body: JSON.stringify(body) });
        }
        return res;
    }

    const contactRes = await postToDataverse("contacts", {
        firstname: data.customer.firstName,
        lastname: data.customer.lastName,
        emailaddress1: data.customer.email
    });
    const contactId = contactRes.headers.get("OData-EntityId").match(/\(([^)]+)\)/)[1];

    await postToDataverse("tot_registrations", {
        "tot_Contact@odata.bind": `/contacts(${contactId})`,
        tot_externalid: data.registrationId,
        tot_campaigncode: data.campaignCode,
        tot_eventid: data.eventId,
        tot_quantity: data.quantity,
        tot_sourcesystem: data.sourceSystem,
        tot_submittedon: data.eventTime,
        tot_tickettype: data.ticketType,
        tot_totalamount: data.totalAmount
    });

    context.log(`Created contact ${contactId} and registration ${data.registrationId}`);
};
