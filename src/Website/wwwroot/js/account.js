// Signs in with passkeys and manages the signed-in user's account, through the feed server's
// api/account endpoints. Passkey options and credentials are sent as WebAuthn's JSON, with
// binary values encoded as base64url.
(function () {
    'use strict';

    var settings = window.nugetfeedAccount || {};
    var api = (settings.pathBase || '') + '/api/account';

    // Binary values

    function toBase64Url(buffer) {
        var bytes = new Uint8Array(buffer);
        var binary = '';
        for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    function fromBase64Url(value) {
        var base64 = value.replace(/-/g, '+').replace(/_/g, '/');
        while (base64.length % 4) base64 += '=';
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes.buffer;
    }

    // The browser rejects null where it expects a value, so leave out what the server didn't set.
    function withoutNulls(value) {
        if (Array.isArray(value)) return value.map(withoutNulls);
        if (value === null || typeof value !== 'object') return value;

        var result = {};
        Object.keys(value).forEach(function (key) {
            if (value[key] !== null) result[key] = withoutNulls(value[key]);
        });
        return result;
    }

    function toDescriptors(descriptors) {
        return (descriptors || []).map(function (d) {
            return Object.assign({}, d, { id: fromBase64Url(d.id) });
        });
    }

    function toCreationOptions(json) {
        json = withoutNulls(json);
        return Object.assign({}, json, {
            challenge: fromBase64Url(json.challenge),
            user: Object.assign({}, json.user, { id: fromBase64Url(json.user.id) }),
            excludeCredentials: toDescriptors(json.excludeCredentials),
        });
    }

    function toRequestOptions(json) {
        json = withoutNulls(json);
        return Object.assign({}, json, {
            challenge: fromBase64Url(json.challenge),
            allowCredentials: toDescriptors(json.allowCredentials),
        });
    }

    function credentialToJson(credential) {
        var response = credential.response;
        var json = {
            id: credential.id,
            rawId: toBase64Url(credential.rawId),
            type: credential.type,
            clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
            response: { clientDataJSON: toBase64Url(response.clientDataJSON) },
        };

        if (response.attestationObject) {
            json.response.attestationObject = toBase64Url(response.attestationObject);
            json.response.transports = response.getTransports ? response.getTransports() : [];
        } else {
            json.response.authenticatorData = toBase64Url(response.authenticatorData);
            json.response.signature = toBase64Url(response.signature);
            if (response.userHandle) json.response.userHandle = toBase64Url(response.userHandle);
        }

        return json;
    }

    // Requests

    function request(method, path, body) {
        var init = { method: method, credentials: 'same-origin', headers: {} };
        if (body !== undefined) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }

        return fetch(api + path, init).then(function (response) {
            var isJson = (response.headers.get('Content-Type') || '').indexOf('json') >= 0;
            if (!response.ok) {
                return response.text().then(function (message) {
                    throw new Error(message || 'Something went wrong (' + response.status + ').');
                });
            }

            return isJson ? response.json() : null;
        });
    }

    // Creates a passkey for a new account, with its user name, or for the signed-in user.
    function createPasskey(optionsPath, body) {
        var path = body.userName ? '/passkeys?userName=' + encodeURIComponent(body.userName) : '/passkeys';

        return request('POST', optionsPath, body)
            .then(function (options) { return navigator.credentials.create({ publicKey: toCreationOptions(options) }); })
            .then(function (credential) { return request('POST', path, credentialToJson(credential)); });
    }

    function signIn(userName) {
        return request('POST', '/signin/options', { userName: userName })
            .then(function (options) { return navigator.credentials.get({ publicKey: toRequestOptions(options) }); })
            .then(function (credential) { return request('POST', '/signin', credentialToJson(credential)); });
    }

    // A name for a new passkey, so the user can tell their passkeys apart.
    function guessPasskeyName() {
        var platform = navigator.userAgentData && navigator.userAgentData.platform;
        return platform ? 'Passkey on ' + platform : 'Passkey';
    }

    // Page helpers

    function showError(error) {
        var element = document.querySelector('[data-account-error]');
        if (!element) return;

        // Cancelling the browser's passkey prompt isn't an error worth showing.
        if (!error || error.name === 'NotAllowedError' || error.name === 'AbortError') {
            element.hidden = true;
            return;
        }

        element.textContent = error.message || String(error);
        element.hidden = false;
    }

    function busy(button, promise) {
        button.disabled = true;
        showError(null);
        return promise
            .catch(showError)
            .then(function () { button.disabled = false; });
    }

    function formatDate(value) {
        return value ? new Date(value).toLocaleDateString() : 'Never';
    }

    function cell(row, text) {
        var td = document.createElement('td');
        td.textContent = text;
        row.appendChild(td);
        return td;
    }

    function removeButton(label, onClick) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'link-button';
        button.textContent = label;
        button.addEventListener('click', function () { busy(button, onClick()); });
        return button;
    }

    function goTo(url) {
        window.location.assign(url || settings.returnUrl || '/');
    }

    // Sign-in page

    function initSignIn(page) {
        if (!window.PublicKeyCredential) {
            page.querySelector('.passkey-unsupported').hidden = false;
            page.querySelector('#signin-button').disabled = true;
            return;
        }

        var signInForm = page.querySelector('#signin-form');
        signInForm.addEventListener('submit', function (event) {
            event.preventDefault();
            var button = signInForm.querySelector('button');
            var userName = signInForm.querySelector('#signin-username').value.trim();

            busy(button, signIn(userName).then(function () { goTo(); }));
        });

        request('GET', '').then(function (account) {
            if (account.signedIn) return goTo();
            if (!account.canSignUp) return;

            page.querySelector('#signup-section').hidden = false;
            page.querySelector('#signup-first').hidden = !account.isFirstUser;
        }).catch(showError);

        var form = page.querySelector('#signup-form');
        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var button = form.querySelector('button');
            var userName = form.querySelector('#signup-username').value.trim();

            busy(button, createPasskey('/signup', { userName: userName, passkeyName: guessPasskeyName() })
                .then(function () { goTo(); }));
        });
    }

    // Account page

    function loadPasskeys(page) {
        return request('GET', '/passkeys').then(function (passkeys) {
            var body = page.querySelector('#passkeys');
            body.textContent = '';

            passkeys.forEach(function (passkey) {
                var row = document.createElement('tr');
                cell(row, passkey.name + (passkey.isBackedUp ? ' (synced)' : ''));
                cell(row, formatDate(passkey.createdAt));
                cell(row, formatDate(passkey.lastUsedAt));
                cell(row, '').appendChild(removeButton('Remove', function () {
                    if (!window.confirm('Remove the passkey "' + passkey.name + '"? You won\'t be able to sign in with it.')) {
                        return Promise.resolve();
                    }

                    return request('DELETE', '/passkeys/' + passkey.id).then(function () { return loadPasskeys(page); });
                }));
                body.appendChild(row);
            });
        });
    }

    function loadApiKeys(page) {
        return request('GET', '/apikeys').then(function (apiKeys) {
            var body = page.querySelector('#apikeys');
            body.textContent = '';

            if (apiKeys.length === 0) {
                var empty = document.createElement('tr');
                cell(empty, 'You have no API keys.').colSpan = 5;
                body.appendChild(empty);
            }

            apiKeys.forEach(function (apiKey) {
                var row = document.createElement('tr');
                cell(row, apiKey.name);
                cell(row, apiKey.prefix + '…').className = 'text-nowrap';
                cell(row, formatDate(apiKey.createdAt));
                cell(row, formatDate(apiKey.lastUsedAt));
                cell(row, '').appendChild(removeButton('Revoke', function () {
                    if (!window.confirm('Revoke the API key "' + apiKey.name + '"? Clients using it will stop working.')) {
                        return Promise.resolve();
                    }

                    return request('DELETE', '/apikeys/' + apiKey.id).then(function () { return loadApiKeys(page); });
                }));
                body.appendChild(row);
            });
        });
    }

    function showNewApiKey(page, key) {
        page.querySelector('#new-apikey-value').textContent = key;
        page.querySelector('#new-apikey-config').textContent = [
            '<packageSources>',
            '  <add key="NuGetFeed" value="' + (settings.serviceIndexUrl || '') + '" />',
            '</packageSources>',
            '<packageSourceCredentials>',
            '  <NuGetFeed>',
            '    <add key="Username" value="' + page.querySelector('#account-name').textContent + '" />',
            '    <add key="ClearTextPassword" value="' + key + '" />',
            '  </NuGetFeed>',
            '</packageSourceCredentials>',
        ].join('\n');
        page.querySelector('#new-apikey').hidden = false;

        var copy = page.querySelector('#copy-apikey');
        copy.onclick = function () { window.nugetfeed.copyTextToClipboard(key, copy); };
    }

    function initAccount(page) {
        request('GET', '').then(function (account) {
            if (!account.signedIn) return goTo((settings.pathBase || '') + '/account/signin?returnUrl=' + encodeURIComponent(window.location.pathname));
            page.querySelector('#account-role').textContent = account.role;
        }).catch(showError);

        loadPasskeys(page).catch(showError);
        loadApiKeys(page).catch(showError);

        var passkeyForm = page.querySelector('#add-passkey-form');
        passkeyForm.addEventListener('submit', function (event) {
            event.preventDefault();
            var input = passkeyForm.querySelector('#passkey-name');
            busy(passkeyForm.querySelector('button'), createPasskey('/passkeys/options', { passkeyName: input.value.trim() || guessPasskeyName() })
                .then(function () {
                    input.value = '';
                    return loadPasskeys(page);
                }));
        });

        var apiKeyForm = page.querySelector('#create-apikey-form');
        apiKeyForm.addEventListener('submit', function (event) {
            event.preventDefault();
            var input = apiKeyForm.querySelector('#apikey-name');
            busy(apiKeyForm.querySelector('button'), request('POST', '/apikeys', { name: input.value.trim() })
                .then(function (apiKey) {
                    input.value = '';
                    showNewApiKey(page, apiKey.key);
                    return loadApiKeys(page);
                }));
        });

        var signOutButton = page.querySelector('#signout-button');
        signOutButton.addEventListener('click', function () {
            busy(signOutButton, request('POST', '/signout').then(function () { goTo(); }));
        });
    }

    var signInPage = document.getElementById('signin-page');
    if (signInPage) initSignIn(signInPage);

    var accountPage = document.getElementById('account-page');
    if (accountPage) initAccount(accountPage);
})();
