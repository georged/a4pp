const { DefaultAzureCredential } = require("@azure/identity");

const credential = new DefaultAzureCredential();

function getEnvironmentUrl() {
  const url = process.env.DATAVERSE_ENVIRONMENT_URL;
  if (!url) {
    throw new Error("DATAVERSE_ENVIRONMENT_URL is not configured.");
  }
  return url.replace(/\/$/, "");
}

async function getAccessToken() {
  const environmentUrl = getEnvironmentUrl();
  const scope = `${environmentUrl}/.default`;
  const token = await credential.getToken(scope);
  if (!token) {
    throw new Error("Failed to acquire Dataverse access token.");
  }
  return token.token;
}

function escapeODataString(value) {
  return String(value ?? "").replace(/'/g, "''");
}

async function request(path, options = {}) {
  const environmentUrl = getEnvironmentUrl();
  const token = await getAccessToken();

  const response = await fetch(`${environmentUrl}${path}`, {
    ...options,
    headers: {
      "Authorization": `Bearer ${token}`,
      "Accept": "application/json",
      "Content-Type": "application/json",
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(`Dataverse request failed (${response.status}): ${errorBody}`);
  }

  if (response.status === 204) {
    return null;
  }

  return response.json();
}

async function upsertContact(customer) {
  const email = escapeODataString(customer?.email || "");
  if (!email) {
    throw new Error("customer.email is required.");
  }

  const existing = await request(`/api/data/v9.2/contacts?$select=contactid&$top=1&$filter=emailaddress1 eq '${email}'`);
  if (Array.isArray(existing?.value) && existing.value.length > 0) {
    return existing.value[0].contactid;
  }

  const created = await request("/api/data/v9.2/contacts", {
    method: "POST",
    body: JSON.stringify({
      firstname: customer?.firstName || "",
      lastname: customer?.lastName || "",
      emailaddress1: customer?.email || ""
    })
  });

  return created.contactid;
}

async function registrationExists(registrationId) {
  const externalId = escapeODataString(registrationId);
  const existing = await request(`/api/data/v9.2/tot_registrations?$select=tot_registrationid&$top=1&$filter=tot_externalid eq '${externalId}'`);
  return Array.isArray(existing?.value) && existing.value.length > 0;
}

async function createRegistration(registration, contactId) {
  await request("/api/data/v9.2/tot_registrations", {
    method: "POST",
    body: JSON.stringify({
      tot_externalid: registration.registrationId,
      tot_eventid: registration.eventId,
      tot_sourcesystem: registration.sourceSystem,
      tot_submittedon: registration.eventTime,
      tot_campaigncode: registration.campaignCode,
      tot_tickettype: registration.ticketType,
      tot_quantity: registration.quantity,
      tot_totalamount: registration.totalAmount,
      "tot_contact@odata.bind": `/contacts(${contactId})`
    })
  });
}

module.exports = {
  upsertContact,
  registrationExists,
  createRegistration
};
