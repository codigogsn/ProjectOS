const statusBar = document.getElementById('statusBar');
const projectList = document.getElementById('projectList');
const projectDetail = document.getElementById('projectDetail');

let currentProjectId = null;

function getOrgId() {
    const val = document.getElementById('orgId').value.trim();
    if (!val) {
        showStatus('Please enter an Organization ID', 'error');
        return null;
    }
    return val;
}

function showStatus(message, type) {
    statusBar.textContent = message;
    statusBar.className = 'topbar-status ' + (type || '');
    if (type === 'success') {
        setTimeout(() => {
            if (statusBar.textContent === message) {
                statusBar.textContent = '';
                statusBar.className = 'topbar-status';
            }
        }, 5000);
    }
}

function formatDate(dateStr) {
    if (!dateStr) return '-';
    const d = new Date(dateStr);
    return d.toLocaleDateString('en-US', {
        month: 'short', day: 'numeric', year: 'numeric',
        hour: '2-digit', minute: '2-digit'
    });
}

function formatRelative(dateStr) {
    if (!dateStr) return '-';
    const d = new Date(dateStr);
    const now = new Date();
    const diffMs = now - d;
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 1) return 'just now';
    if (diffMins < 60) return diffMins + 'm ago';
    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return diffHours + 'h ago';
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 30) return diffDays + 'd ago';
    return formatDate(dateStr);
}

function statusClass(status) {
    return (status || 'draft').toLowerCase().replace(/\s/g, '');
}

async function apiCall(url, method) {
    const res = await fetch(url, { method: method || 'GET' });
    if (!res.ok) {
        const text = await res.text();
        throw new Error(text || res.statusText);
    }
    return res.json();
}

// ---- Projects list ----

async function loadProjects() {
    const orgId = getOrgId();
    if (!orgId) return;

    const header = projectList.querySelector('.sidebar-header');
    projectList.innerHTML = '';
    projectList.appendChild(header);
    projectList.insertAdjacentHTML('beforeend', '<div class="loading">Loading projects...</div>');

    try {
        const projects = await apiCall('/api/projects?organizationId=' + orgId);

        projectList.innerHTML = '';
        projectList.appendChild(header);

        if (projects.length === 0) {
            projectList.insertAdjacentHTML('beforeend',
                '<div class="sidebar-empty">No projects found. Try syncing emails and grouping.</div>');
            return;
        }

        header.textContent = 'Projects (' + projects.length + ')';

        for (const p of projects) {
            const div = document.createElement('div');
            div.className = 'project-item' + (p.id === currentProjectId ? ' active' : '');
            div.onclick = () => loadProjectDetail(p.id);
            div.innerHTML =
                '<div class="project-item-name">' + escapeHtml(p.name) + '</div>' +
                '<div class="project-item-meta">' +
                    '<span class="status-badge ' + statusClass(p.status) + '">' + escapeHtml(p.status) + '</span>' +
                    '<span>' + p.emailCount + ' emails</span>' +
                    '<span>' + formatRelative(p.lastActivityAtUtc) + '</span>' +
                '</div>';
            projectList.appendChild(div);
        }

        showStatus('Loaded ' + projects.length + ' projects', 'success');
    } catch (err) {
        projectList.innerHTML = '';
        projectList.appendChild(header);
        projectList.insertAdjacentHTML('beforeend',
            '<div class="sidebar-empty">Error loading projects</div>');
        showStatus('Error: ' + err.message, 'error');
    }
}

// ---- Project detail ----

async function loadProjectDetail(projectId) {
    currentProjectId = projectId;

    // Highlight active item
    document.querySelectorAll('.project-item').forEach(el => el.classList.remove('active'));
    const items = document.querySelectorAll('.project-item');
    for (const item of items) {
        if (item.onclick && item.onclick.toString().includes(projectId)) {
            item.classList.add('active');
        }
    }

    projectDetail.innerHTML = '<div class="loading">Loading project...</div>';

    try {
        const p = await apiCall('/api/projects/' + projectId);

        let html = '<div class="detail-header">' +
            '<h2>' + escapeHtml(p.name) + '</h2>' +
            '<div class="detail-meta">' +
                '<span class="status-badge ' + statusClass(p.status) + '">' + escapeHtml(p.status) + '</span>' +
                '<span>Created: ' + formatDate(p.createdAtUtc) + '</span>' +
                '<span>Last activity: ' + formatRelative(p.lastActivityAtUtc) + '</span>' +
                '<span>' + p.emailCount + ' emails</span>' +
            '</div>' +
            '</div>';

        if (p.emails && p.emails.length > 0) {
            html += '<div class="emails-header">Email Timeline (' + p.emails.length + ')</div>';

            for (const e of p.emails) {
                html += '<div class="email-card">' +
                    '<div class="email-card-header">' +
                        '<div class="email-subject">' + escapeHtml(e.subject) + '</div>' +
                        '<div class="email-date">' + formatDate(e.sentAtUtc) + '</div>' +
                    '</div>' +
                    '<div class="email-from">' +
                        escapeHtml(e.fromName ? e.fromName + ' <' + e.fromEmail + '>' : e.fromEmail) +
                    '</div>' +
                    '<div class="email-body">' + escapeHtml(e.bodyPreview) + '</div>' +
                    (e.assignmentSource ?
                        '<div class="email-badge">' + escapeHtml(e.assignmentSource) +
                        (e.assignmentConfidence ? ' (' + (e.assignmentConfidence * 100).toFixed(0) + '%)' : '') +
                        '</div>' : '') +
                '</div>';
            }
        } else {
            html += '<div class="sidebar-empty">No emails assigned to this project</div>';
        }

        projectDetail.innerHTML = html;
    } catch (err) {
        projectDetail.innerHTML = '<div class="detail-empty">Error loading project details</div>';
        showStatus('Error: ' + err.message, 'error');
    }
}

// ---- Sync & Group ----

async function syncEmails() {
    const orgId = getOrgId();
    if (!orgId) return;

    showStatus('Syncing emails...', '');
    setButtonsDisabled(true);

    try {
        const result = await apiCall('/api/emails/sync?organizationId=' + orgId, 'POST');
        showStatus('Sync: ' + result.saved + ' new, ' + result.duplicates + ' duplicates', 'success');
    } catch (err) {
        showStatus('Sync failed: ' + err.message, 'error');
    } finally {
        setButtonsDisabled(false);
    }
}

async function groupProjects() {
    const orgId = getOrgId();
    if (!orgId) return;

    showStatus('Grouping projects...', '');
    setButtonsDisabled(true);

    try {
        const result = await apiCall('/api/projects/group?organizationId=' + orgId, 'POST');
        showStatus(
            'Grouped: ' + result.assignedToExisting + ' existing, ' +
            result.newProjectsCreated + ' new projects', 'success');
        await loadProjects();
    } catch (err) {
        showStatus('Grouping failed: ' + err.message, 'error');
    } finally {
        setButtonsDisabled(false);
    }
}

function setButtonsDisabled(disabled) {
    document.querySelectorAll('.topbar-controls button').forEach(b => b.disabled = disabled);
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
