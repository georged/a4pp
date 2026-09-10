let cachedToken = null;
let cachedTokenExpiresAt = 0;

async function getAccessToken() {
    if (cachedToken && Date.now() < cachedTokenExpiresAt) {
        return cachedToken;
    }

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
    const { access_token, expires_in } = await tokenRes.json();
    cachedToken = access_token;
    cachedTokenExpiresAt = Date.now() + (expires_in - 60) * 1000;
    return cachedToken;
}

// Shared by every function that talks to Dataverse, so the 429/Retry-After handling
// (needed under burst load) lives in one place instead of being copy-pasted per activity.
async function dataverseFetch(path, options = {}) {
    const env = process.env.DATAVERSE_ENVIRONMENT_URL;
    const accessToken = await getAccessToken();
    const headers = {
        "Authorization": `Bearer ${accessToken}`,
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
        "Accept": "application/json",
        ...options.headers
    };

    const url = path.startsWith("http") ? path : `${env}/api/data/v9.2/${path}`;
    let res = await fetch(url, { ...options, headers });
    while (res.status === 429) {
        const waitSeconds = Number(res.headers.get("Retry-After")) || 5;
        await new Promise((resolve) => setTimeout(resolve, waitSeconds * 1000));
        res = await fetch(url, { ...options, headers });
    }
    return res;
}

module.exports = { getAccessToken, dataverseFetch };
