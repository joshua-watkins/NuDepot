// Manages packages from the package and upload pages, through the feed's endpoints, as the signed-in user. The
// page only shows what the user may do. Written for browsers that support passkeys.
(() => {
    'use strict';

    const settings = window.nudepotManage ?? {};
    const base = settings.pathBase ?? '';
    const { request, showError, busy } = window.nudepot;
    const encode = encodeURIComponent;

    // Buttons that call one of the feed's endpoints, like unlisting a version, then show the page again.
    for (const button of document.querySelectorAll('[data-manage-url]')) {
        button.addEventListener('click', () => {
            const confirmation = button.dataset.manageConfirm;
            if (confirmation && !window.confirm(confirmation)) return;

            busy(button, request(button.dataset.manageMethod, button.dataset.manageUrl).then(() => window.location.reload()));
        });
    }

    // Deprecation

    const deprecationForm = document.getElementById('deprecation-form');
    deprecationForm?.addEventListener('submit', (event) => {
        event.preventDefault();
        const form = new FormData(deprecationForm);
        const reasons = form.getAll('reasons');
        if (reasons.length === 0) {
            showError(new Error('Choose at least one reason.'));
            return;
        }

        const url = `${base}/api/package/${encode(settings.packageId)}/${encode(settings.version)}/deprecation`;
        const body = {
            reasons,
            message: form.get('message').trim() || null,
            alternatePackageId: form.get('alternatePackageId').trim() || null,
        };
        busy(deprecationForm.querySelector('button[type="submit"]'), request('PUT', url, body).then(() => window.location.reload()));
    });

    // Vulnerabilities

    const severities = ['Low', 'Moderate', 'High', 'Critical'];
    const vulnerabilities = document.getElementById('vulnerabilities');

    const loadVulnerabilities = async () => {
        const advisories = await request('GET', `${base}/api/vulnerabilities?packageId=${encode(settings.packageId)}`);
        vulnerabilities.textContent = '';

        if (advisories.length === 0) {
            const row = vulnerabilities.insertRow();
            const empty = row.insertCell();
            empty.colSpan = 4;
            empty.textContent = 'No known vulnerabilities.';
        }

        for (const advisory of advisories) {
            const row = vulnerabilities.insertRow();
            row.insertCell().textContent = advisory.versions;
            row.insertCell().textContent = severities[advisory.severity] ?? advisory.severity;

            const link = document.createElement('a');
            link.href = advisory.url;
            link.textContent = advisory.url;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            row.insertCell().appendChild(link);

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'link-button';
            remove.textContent = 'Remove';
            remove.addEventListener('click', () => {
                if (!window.confirm(`Remove the vulnerability ${advisory.url}?`)) return;

                const url = `${base}/api/vulnerabilities?packageId=${encode(advisory.packageId)}&url=${encode(advisory.url)}`;
                busy(remove, request('DELETE', url).then(() => window.location.reload()));
            });
            const actions = row.insertCell();
            actions.className = 'text-right';
            actions.appendChild(remove);
        }
    };

    if (vulnerabilities) loadVulnerabilities().catch(showError);

    const vulnerabilityForm = document.getElementById('vulnerability-form');
    vulnerabilityForm?.addEventListener('submit', (event) => {
        event.preventDefault();
        const form = new FormData(vulnerabilityForm);
        const body = {
            packageId: settings.packageId,
            versions: form.get('versions').trim(),
            url: form.get('url').trim(),
            severity: Number(form.get('severity')),
        };
        busy(vulnerabilityForm.querySelector('button'), request('PUT', `${base}/api/vulnerabilities`, body).then(() => window.location.reload()));
    });

    // Uploads

    // What a push's status means, as the feed's publish endpoints answer.
    const uploadResult = (file, error) => {
        const symbols = file.name.toLowerCase().endsWith('.snupkg');
        if (!error) return `${file.name}: uploaded.`;

        switch (error.status) {
            case 400: return `${file.name}: isn't a valid ${symbols ? 'symbol package' : 'package'}.`;
            case 404: return `${file.name}: its package isn't on the feed. Upload the package first.`;
            case 409: return `${file.name}: that version is already on the feed.`;
            case 403: return `${file.name}: you may not push packages.`;
            default: return `${file.name}: ${error.message}`;
        }
    };

    const uploadForm = document.getElementById('upload-form');
    uploadForm?.addEventListener('submit', (event) => {
        event.preventDefault();
        const input = uploadForm.querySelector('input[type="file"]');
        const results = document.getElementById('upload-results');
        results.textContent = '';

        busy(uploadForm.querySelector('button'), (async () => {
            for (const file of input.files) {
                const symbols = file.name.toLowerCase().endsWith('.snupkg');
                const body = new FormData();
                body.append('package', file, file.name);

                const item = document.createElement('li');
                try {
                    await request('PUT', `${base}/api/v2/${symbols ? 'symbol' : 'package'}`, body);
                    item.className = 'text-success';
                    item.textContent = uploadResult(file, null);
                } catch (error) {
                    item.className = 'text-danger';
                    item.textContent = uploadResult(file, error);
                }
                results.appendChild(item);
            }

            input.value = '';
        })());
    });
})();
