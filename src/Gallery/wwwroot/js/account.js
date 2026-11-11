// Signs in with passkeys and manages the signed-in user's account, through the feed server's
// api/account endpoints. Passkey options and credentials are sent as WebAuthn's JSON, with
// binary values encoded as base64url. Written for browsers that support passkeys.
(() => {
    'use strict';

    const settings = window.nudepotAccount ?? {};
    const api = `${settings.pathBase ?? ''}/api/account`;

    // Binary values

    const toBase64Url = (buffer) => {
        const binary = String.fromCharCode(...new Uint8Array(buffer));
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    };

    const fromBase64Url = (value) => {
        const base64 = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(value.length / 4) * 4, '=');
        return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0)).buffer;
    };

    // The browser rejects null where it expects a value, so leave out what the server didn't set.
    const withoutNulls = (value) => {
        if (Array.isArray(value)) return value.map(withoutNulls);
        if (value === null || typeof value !== 'object') return value;

        return Object.fromEntries(Object.entries(value)
            .filter(([, item]) => item !== null)
            .map(([key, item]) => [key, withoutNulls(item)]));
    };

    const toDescriptors = (descriptors) => (descriptors ?? []).map((d) => ({ ...d, id: fromBase64Url(d.id) }));

    const toCreationOptions = (json) => {
        json = withoutNulls(json);
        return {
            ...json,
            challenge: fromBase64Url(json.challenge),
            user: { ...json.user, id: fromBase64Url(json.user.id) },
            excludeCredentials: toDescriptors(json.excludeCredentials),
        };
    };

    const toRequestOptions = (json) => {
        json = withoutNulls(json);
        return {
            ...json,
            challenge: fromBase64Url(json.challenge),
            allowCredentials: toDescriptors(json.allowCredentials),
        };
    };

    const credentialToJson = (credential) => {
        const { response } = credential;
        const json = {
            id: credential.id,
            rawId: toBase64Url(credential.rawId),
            type: credential.type,
            clientExtensionResults: credential.getClientExtensionResults(),
            response: { clientDataJSON: toBase64Url(response.clientDataJSON) },
        };

        if (response.attestationObject) {
            json.response.attestationObject = toBase64Url(response.attestationObject);
            json.response.transports = response.getTransports();
        } else {
            json.response.authenticatorData = toBase64Url(response.authenticatorData);
            json.response.signature = toBase64Url(response.signature);
            if (response.userHandle) json.response.userHandle = toBase64Url(response.userHandle);
        }

        return json;
    };

    // Requests

    const request = async (method, path, body) => {
        const init = { method, credentials: 'same-origin', headers: {} };
        if (body !== undefined) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }

        const response = await fetch(api + path, init);
        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || `Something went wrong (${response.status}).`);
        }

        const isJson = (response.headers.get('Content-Type') ?? '').includes('json');
        return isJson ? response.json() : null;
    };

    // Creates a passkey for a new account, with its user name, or for the signed-in user.
    const createPasskey = async (optionsPath, body) => {
        const path = body.userName ? `/passkeys?userName=${encodeURIComponent(body.userName)}` : '/passkeys';

        const options = await request('POST', optionsPath, body);
        const credential = await navigator.credentials.create({ publicKey: toCreationOptions(options) });
        return request('POST', path, credentialToJson(credential));
    };

    const signIn = async (userName) => {
        const options = await request('POST', '/signin/options', { userName });
        const credential = await navigator.credentials.get({ publicKey: toRequestOptions(options) });
        return request('POST', '/signin', credentialToJson(credential));
    };

    // A name for a new passkey, so the user can tell their passkeys apart. Only Chromium browsers say their platform.
    const guessPasskeyName = () => {
        const platform = navigator.userAgentData?.platform;
        return platform ? `Passkey on ${platform}` : 'Passkey';
    };

    // Page helpers

    const showError = (error) => {
        const element = document.querySelector('[data-account-error]');
        if (!element) return;

        // Cancelling the browser's passkey prompt isn't an error worth showing.
        if (!error || error.name === 'NotAllowedError' || error.name === 'AbortError') {
            element.hidden = true;
            return;
        }

        element.textContent = error.message || String(error);
        element.hidden = false;
    };

    const busy = async (button, promise) => {
        button.disabled = true;
        showError(null);
        try {
            await promise;
        } catch (error) {
            showError(error);
        } finally {
            button.disabled = false;
        }
    };

    const formatDate = (value) => value ? new Date(value).toLocaleDateString() : 'Never';

    const cell = (row, text) => {
        const td = document.createElement('td');
        td.textContent = text;
        row.appendChild(td);
        return td;
    };

    const removeButton = (label, onClick) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'link-button';
        button.textContent = label;
        button.addEventListener('click', () => busy(button, onClick()));
        return button;
    };

    const goTo = (url) => window.location.assign(url || settings.returnUrl || '/');

    // Sign-in page

    const initSignIn = (page) => {
        // Passkeys need HTTPS, or localhost: over HTTP, browsers don't offer them.
        if (!window.PublicKeyCredential) {
            page.querySelector('.passkey-unsupported').hidden = false;
            page.querySelector('#signin-button').disabled = true;
            return;
        }

        const signInForm = page.querySelector('#signin-form');
        signInForm.addEventListener('submit', (event) => {
            event.preventDefault();
            const userName = signInForm.querySelector('#signin-username').value.trim();

            busy(signInForm.querySelector('button'), signIn(userName).then(() => goTo()));
        });

        request('GET', '')
            .then((account) => {
                if (account.signedIn) return goTo();
                if (!account.canSignUp) return;

                page.querySelector('#signup-section').hidden = false;
                page.querySelector('#signup-first').hidden = !account.isFirstUser;
            })
            .catch(showError);

        const form = page.querySelector('#signup-form');
        form.addEventListener('submit', (event) => {
            event.preventDefault();
            const userName = form.querySelector('#signup-username').value.trim();

            busy(form.querySelector('button'), createPasskey('/signup', { userName, passkeyName: guessPasskeyName() }).then(() => goTo()));
        });
    };

    // Account page

    const loadPasskeys = async (page) => {
        const passkeys = await request('GET', '/passkeys');
        const body = page.querySelector('#passkeys');
        body.textContent = '';

        for (const passkey of passkeys) {
            const row = document.createElement('tr');
            cell(row, passkey.name + (passkey.isBackedUp ? ' (synced)' : ''));
            cell(row, formatDate(passkey.createdAt));
            cell(row, formatDate(passkey.lastUsedAt));
            cell(row, '').appendChild(removeButton('Remove', async () => {
                if (!window.confirm(`Remove the passkey "${passkey.name}"? You won't be able to sign in with it.`)) return;

                await request('DELETE', `/passkeys/${passkey.id}`);
                await loadPasskeys(page);
            }));
            body.appendChild(row);
        }
    };

    const loadApiKeys = async (page) => {
        const apiKeys = await request('GET', '/apikeys');
        const body = page.querySelector('#apikeys');
        body.textContent = '';

        if (apiKeys.length === 0) {
            const empty = document.createElement('tr');
            cell(empty, 'You have no API keys.').colSpan = 5;
            body.appendChild(empty);
        }

        for (const apiKey of apiKeys) {
            const row = document.createElement('tr');
            cell(row, apiKey.name);
            cell(row, `${apiKey.prefix}…`).className = 'text-nowrap';
            cell(row, formatDate(apiKey.createdAt));
            cell(row, formatDate(apiKey.lastUsedAt));
            cell(row, '').appendChild(removeButton('Revoke', async () => {
                if (!window.confirm(`Revoke the API key "${apiKey.name}"? Clients using it will stop working.`)) return;

                await request('DELETE', `/apikeys/${apiKey.id}`);
                await loadApiKeys(page);
            }));
            body.appendChild(row);
        }
    };

    const loadSessions = async (page) => {
        const sessions = await request('GET', '/sessions');
        const body = page.querySelector('#sessions');
        body.textContent = '';

        for (const session of sessions) {
            const row = document.createElement('tr');
            cell(row, session.isCurrent ? 'This browser' : 'Another browser');
            cell(row, formatDate(session.createdAt));
            cell(row, formatDate(session.lastUsedAt));
            const actions = cell(row, '');
            if (!session.isCurrent) {
                actions.appendChild(removeButton('Sign out', async () => {
                    await request('DELETE', `/sessions/${session.id}`);
                    await loadSessions(page);
                }));
            }
            body.appendChild(row);
        }
    };

    const showNewApiKey = (page, key) => {
        page.querySelector('#new-apikey-value').textContent = key;
        page.querySelector('#new-apikey-config').textContent = [
            '<packageSources>',
            `  <add key="NuDepot" value="${settings.serviceIndexUrl ?? ''}" />`,
            '</packageSources>',
            '<packageSourceCredentials>',
            '  <NuDepot>',
            `    <add key="Username" value="${page.querySelector('#account-name').textContent}" />`,
            `    <add key="ClearTextPassword" value="${key}" />`,
            '  </NuDepot>',
            '</packageSourceCredentials>',
        ].join('\n');
        page.querySelector('#new-apikey').hidden = false;

        const copy = page.querySelector('#copy-apikey');
        copy.onclick = () => window.nudepot.copyTextToClipboard(key, copy);
    };

    const initAccount = (page) => {
        request('GET', '')
            .then((account) => {
                if (!account.signedIn) {
                    return goTo(`${settings.pathBase ?? ''}/account/signin?returnUrl=${encodeURIComponent(window.location.pathname)}`);
                }

                page.querySelector('#account-role').textContent = account.role;
            })
            .catch(showError);

        loadPasskeys(page).catch(showError);
        loadApiKeys(page).catch(showError);
        loadSessions(page).catch(showError);

        const apiKeyForm = page.querySelector('#create-apikey-form');
        apiKeyForm.addEventListener('submit', (event) => {
            event.preventDefault();
            const input = apiKeyForm.querySelector('#apikey-name');

            busy(apiKeyForm.querySelector('button'), (async () => {
                const apiKey = await request('POST', '/apikeys', { name: input.value.trim() });
                input.value = '';
                showNewApiKey(page, apiKey.key);
                await loadApiKeys(page);
            })());
        });

        const passkeyForm = page.querySelector('#add-passkey-form');
        passkeyForm.addEventListener('submit', (event) => {
            event.preventDefault();
            const input = passkeyForm.querySelector('#passkey-name');

            busy(passkeyForm.querySelector('button'), (async () => {
                await createPasskey('/passkeys/options', { passkeyName: input.value.trim() || guessPasskeyName() });
                input.value = '';
                await loadPasskeys(page);
            })());
        });

        const signOutButton = page.querySelector('#signout-button');
        signOutButton.addEventListener('click', () => busy(signOutButton, request('POST', '/signout').then(() => goTo())));
    };

    const signInPage = document.getElementById('signin-page');
    if (signInPage) initSignIn(signInPage);

    const accountPage = document.getElementById('account-page');
    if (accountPage) initAccount(accountPage);
})();
