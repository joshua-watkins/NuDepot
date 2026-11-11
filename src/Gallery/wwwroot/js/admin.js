// The administration page: the feed's users, signing and settings, through the feed's api/admin endpoints, as the
// signed-in administrator. Written for browsers that support passkeys.
(() => {
    'use strict';

    const settings = window.nudepotManage ?? {};
    const api = `${settings.pathBase ?? ''}/api/admin`;
    const { request, showError, busy } = window.nudepot;
    const page = document.getElementById('admin-page');
    if (!page) return;

    const formatDate = (value) => value ? new Date(value).toLocaleDateString() : 'Never';
    const formatTime = (value) => new Date(value).toLocaleString();

    const button = (label, className, onClick) => {
        const element = document.createElement('button');
        element.type = 'button';
        element.className = className;
        element.textContent = label;
        element.addEventListener('click', () => onClick(element));
        return element;
    };

    // Users

    const roles = ['ReadOnly', 'ReadWrite', 'Admin'];

    const loadUsers = async () => {
        const users = await request('GET', `${api}/users`);
        const body = page.querySelector('#users');
        body.textContent = '';

        for (const user of users) {
            const row = body.insertRow();
            row.insertCell().textContent = user.userName + (user.isCurrentUser ? ' (you)' : '');

            const role = document.createElement('select');
            role.className = 'form-control input-sm';
            role.setAttribute('aria-label', `${user.userName}'s role`);
            for (const name of roles) role.add(new Option(name, name, false, name === user.role));
            role.addEventListener('change', () => busy(role, (async () => {
                try {
                    await request('PUT', `${api}/users/${user.id}`, { role: role.value });
                } finally {
                    await loadUsers();
                }
            })()));
            row.insertCell().appendChild(role);

            row.insertCell().textContent = formatDate(user.createdAt);
            row.insertCell().textContent = formatDate(user.lastSignedInAt);
            row.insertCell().textContent = user.passkeys;
            row.insertCell().textContent = user.apiKeys;
            row.insertCell().textContent = user.sessions;

            const actions = row.insertCell();
            actions.className = 'text-right text-nowrap';
            if (user.sessions > 0 && !user.isCurrentUser) {
                actions.appendChild(button('Sign out', 'link-button', (element) => {
                    if (!window.confirm(`Sign ${user.userName} out of every browser?`)) return;
                    busy(element, request('DELETE', `${api}/users/${user.id}/sessions`).then(loadUsers));
                }));
            }
            if (user.apiKeys > 0) {
                actions.appendChild(button('Revoke API keys', 'link-button', (element) => {
                    if (!window.confirm(`Revoke all of ${user.userName}'s API keys? Clients using them will stop working.`)) return;
                    busy(element, request('DELETE', `${api}/users/${user.id}/apikeys`).then(loadUsers));
                }));
            }
            if (!user.isCurrentUser) {
                actions.appendChild(button('Delete', 'link-button text-danger', (element) => {
                    if (!window.confirm(`Delete ${user.userName}? They're signed out, and their passkeys and API keys stop working.`)) return;
                    busy(element, request('DELETE', `${api}/users/${user.id}`).then(loadUsers));
                }));
            }
        }
    };

    // Signing

    const certificateStatus = (certificate) => {
        if (certificate.isCurrent) return 'Signs packages';
        if (certificate.revokedAt) return `Revoked ${formatDate(certificate.revokedAt)}`;
        if (new Date(certificate.notAfter) < new Date()) return 'Expired';
        return 'Valid';
    };

    const showSigning = (signing) => {
        page.querySelector('#signing-off').hidden = signing.isEnabled;
        page.querySelector('#signing-on').hidden = !signing.isEnabled;
        page.querySelector('#rotate-section').hidden = !signing.generatesCertificates;

        const body = page.querySelector('#certificates');
        body.textContent = '';
        for (const certificate of signing.certificates) {
            const row = body.insertRow();
            const fingerprint = document.createElement('code');
            fingerprint.textContent = certificate.fingerprint;
            row.insertCell().appendChild(fingerprint);
            row.insertCell().textContent = certificate.subject ?? '';
            row.insertCell().textContent = `${formatDate(certificate.notBefore)} to ${formatDate(certificate.notAfter)}`;
            row.insertCell().textContent = certificateStatus(certificate);
        }
    };

    const loadSigning = async () => showSigning(await request('GET', `${api}/signing`));

    const rotateButton = page.querySelector('#rotate-button');
    rotateButton.addEventListener('click', () => {
        if (!window.confirm('Replace this year\'s signing certificate? The current one is revoked.')) return;
        busy(rotateButton, request('POST', `${api}/signing/rotate`).then(showSigning));
    });

    // Settings

    const anonymousRoles = {
        None: 'Nothing: the feed is private',
        ReadOnly: 'Read',
        ReadWrite: 'Read, push and unlist',
        Admin: 'Everything',
    };

    // A health status, as a label colored like Bootstrap's alerts.
    const healthLabel = (status) => {
        const label = document.createElement('span');
        label.className = `label ${{ Healthy: 'label-success', Degraded: 'label-warning' }[status] ?? 'label-danger'}`;
        label.textContent = status;
        return label;
    };

    const showHealth = (health) => {
        const summary = page.querySelector('#health-status');
        summary.textContent = '';
        summary.appendChild(healthLabel(health.status));

        const body = page.querySelector('#health');
        body.textContent = '';
        for (const check of health.checks) {
            const row = body.insertRow();
            row.insertCell().textContent = check.name;
            row.insertCell().appendChild(healthLabel(check.status));
            row.insertCell().textContent = check.description ?? '';
        }
    };

    const loadStatus = async () => {
        const status = await request('GET', `${api}/status`);
        showHealth(status.health);
        page.querySelector('#settings-folder').textContent = status.settingsFolder;

        const rows = [
            ['Read-only mode', status.isReadOnlyMode ? 'On: nobody may change the packages' : 'Off'],
            ['Deleting a package', status.packageDeletionBehavior === 'HardDelete' ? 'Removes it permanently' : 'Unlists it'],
            ['Pushing a version that exists', status.allowPackageOverwrites ? 'Replaces it, for Admins' : 'Is refused'],
            ['Requests without a key or a signed-in user may', anonymousRoles[status.anonymousRole] ?? status.anonymousRole],
            ['API keys in the settings', status.apiKeyRoles.length > 0 ? status.apiKeyRoles.join(', ') : 'None'],
            ['Signing up', status.allowSignUp ? 'Anyone may create an account' : 'Only the first account'],
            ['Role of new accounts', status.defaultRole],
            ['Repository signing', status.isSigning ? 'On' : 'Off'],
            ['Mirroring', status.isMirroring ? 'On' : 'Off'],
            ['Package storage', status.storagePath ?? 'Not on the file system'],
        ];

        const body = page.querySelector('#settings');
        body.textContent = '';
        for (const [name, value] of rows) {
            const row = body.insertRow();
            const header = document.createElement('th');
            header.scope = 'row';
            header.textContent = name;
            row.appendChild(header);
            row.insertCell().textContent = value;
        }

        page.querySelector('#mirrors-section').hidden = status.mirrors.length === 0;
        const mirrors = page.querySelector('#mirrors');
        mirrors.textContent = '';
        for (const mirror of status.mirrors) {
            const row = mirrors.insertRow();
            row.insertCell().textContent = mirror.name;
            row.insertCell().textContent = mirror.packageSource;
            row.insertCell().textContent = mirror.whenUnreachable === 'Stop' ? 'Stop at it' : 'Skip it';
            row.insertCell().textContent = mirror.unreachableUntil
                ? `Couldn't be reached: not asked until ${formatTime(mirror.unreachableUntil)}`
                : 'Asked';
        }
    };

    loadUsers().catch(showError);
    loadSigning().catch(showError);
    loadStatus().catch(showError);
})();
