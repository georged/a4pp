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

module.exports = async function (context, message) {
    const data = typeof message === "string" ? JSON.parse(message) : message;
    context.log(`Processing registration ${data.registrationId} for ${data.customer.email}`);

    const env = process.env.DATAVERSE_ENVIRONMENT_URL;
    const accessToken = await getAccessToken(env);
    const dataverseHeaders = {
        "Authorization": `Bearer ${accessToken}`,
        "Content-Type": "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
    };

    // Deep insert: creates the contact and its linked tot_registration in a single call.
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

    let res = await fetch(`${env}/api/data/v9.2/contacts`, { method: "POST", headers: dataverseHeaders, body: JSON.stringify(body) });
    while (res.status === 429) {
        const waitSeconds = Number(res.headers.get("Retry-After")) || 5;
        context.log(`Throttled by Dataverse, retrying in ${waitSeconds}s`);
        await new Promise((resolve) => setTimeout(resolve, waitSeconds * 1000));
        res = await fetch(`${env}/api/data/v9.2/contacts`, { method: "POST", headers: dataverseHeaders, body: JSON.stringify(body) });
    }
    const contactId = res.headers.get("OData-EntityId").match(/\(([^)]+)\)/)[1];

    context.log(`Created contact ${contactId} and registration ${data.registrationId}`);
};
