window.triggerFileInput = (elementId) => {
    const element = document.getElementById(elementId);
    if (element) element.click();
};

window.downloadFile = (fileName, content) => {
    const blob = new Blob([content], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

window.GoogleAuth = {
    init: function (clientId) {
        window.googleClientId = clientId;
        google.accounts.id.initialize({
            client_id: clientId,
            auto_select: false
        });
        console.log("✅ GoogleAuth initialized with Client ID: " + clientId);
    },

    requestToken: function (scope) {
        return new Promise((resolve, reject) => {
            if (!window.googleClientId) {
                reject("Client ID not set. Please configure it first.");
                return;
            }

            try {
                const tokenClient = google.accounts.oauth2.initTokenClient({
                    client_id: window.googleClientId,
                    scope: scope,
                    prompt: "",                    // Use empty for smoother experience (or "consent")
                    callback: (response) => {
                        if (response.error !== undefined) {
                            console.error("Google Token Error:", response.error);
                            reject(response.error);
                        } else if (response.access_token) {
                            console.log("✅ Access Token successfully received");
                            resolve(response.access_token);
                        } else {
                            reject("No access token in response");
                        }
                    }
                });

                tokenClient.requestAccessToken();
            } catch (error) {
                console.error("Error initializing token client:", error);
                reject(error.message || "Failed to initialize Google token client");
            }
        });
    },
};