const statusBar = document.getElementById('statusBar');
const projectList = document.getElementById('projectList');
const unassignedList = document.getElementById('unassignedList');
const projectDetail = document.getElementById('projectDetail');

let currentProjectId = null;
let cachedProjects = [];

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

async function apiCallJson(url, method, body) {
    const res = await fetch(url, {
        method: method || 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
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

    projectList.innerHTML = '<div class="sidebar-header">Projects</div><div class="loading">Loading...</div>';

    try {
        const projects = await apiCall('/api/projects?organizationId=' + orgId);
        cachedProjects = projects;

        projectList.innerHTML = '<div class="sidebar-header">Projects (' + projects.length + ')</div>';

        if (projects.length === 0) {
            projectList.insertAdjacentHTML('beforeend',
                '<div class="sidebar-empty">No projects found. Try syncing emails and grouping.</div>');
        } else {
            for (const p of projects) {
                const div = document.createElement('div');
                div.className = 'project-item' + (p.id === currentProjectId ? ' active' : '');
                div.setAttribute('data-id', p.id);
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
        }

        showStatus('Loaded ' + projects.length + ' projects', 'success');
    } catch (err) {
        projectList.innerHTML = '<div class="sidebar-header">Projects</div>' +
            '<div class="sidebar-empty">Error loading projects</div>';
        showStatus('Error: ' + err.message, 'error');
    }

    loadUnassigned();
}

// ---- Unassigned inbox ----

async function loadUnassigned() {
    const orgId = getOrgId();
    if (!orgId) return;

    unassignedList.innerHTML = '<div class="sidebar-header unassigned-header">Unassigned Inbox</div><div class="loading">Loading...</div>';

    try {
        const emails = await apiCall('/api/emails/unassigned?organizationId=' + orgId);

        unassignedList.innerHTML = '<div class="sidebar-header unassigned-header">Unassigned (' + emails.length + ')</div>';

        if (emails.length === 0) {
            unassignedList.insertAdjacentHTML('beforeend',
                '<div class="sidebar-empty">All emails are assigned</div>');
            return;
        }

        for (const e of emails) {
            const div = document.createElement('div');
            div.className = 'unassigned-item';
            div.onclick = () => showUnassignedDetail(e);
            div.innerHTML =
                '<div class="project-item-name">' + escapeHtml(e.subject || '(no subject)') + '</div>' +
                '<div class="project-item-meta">' +
                    '<span>' + escapeHtml(e.fromName || e.fromEmail) + '</span>' +
                    '<span>' + formatRelative(e.sentAtUtc) + '</span>' +
                '</div>';
            unassignedList.appendChild(div);
        }
    } catch (err) {
        unassignedList.innerHTML = '<div class="sidebar-header unassigned-header">Unassigned Inbox</div>' +
            '<div class="sidebar-empty">Error loading</div>';
    }
}

function showUnassignedDetail(email) {
    currentProjectId = null;
    document.querySelectorAll('.project-item').forEach(el => el.classList.remove('active'));

    var projectOptions = cachedProjects.map(p =>
        '<option value="' + p.id + '">' + escapeHtml(p.name) + '</option>'
    ).join('');

    projectDetail.innerHTML =
        '<div class="detail-header">' +
            '<h2>' + escapeHtml(email.subject || '(no subject)') + '</h2>' +
            '<div class="detail-meta">' +
                '<span>From: ' + escapeHtml(email.fromName ? email.fromName + ' <' + email.fromEmail + '>' : email.fromEmail) + '</span>' +
                '<span>Sent: ' + formatDate(email.sentAtUtc) + '</span>' +
            '</div>' +
        '</div>' +
        '<div class="email-card" style="margin-bottom:20px">' +
            '<div class="email-body">' + escapeHtml(email.bodyPreview) + '</div>' +
        '</div>' +
        '<div class="assign-actions">' +
            '<div class="assign-row">' +
                '<select id="assignSelect" class="assign-select">' +
                    '<option value="">-- Select project --</option>' +
                    projectOptions +
                '</select>' +
                '<button class="btn-assign" onclick="assignEmail(\'' + email.id + '\')">Assign</button>' +
            '</div>' +
            '<button class="btn-create-project" onclick="createProjectFromEmail(\'' + email.id + '\')">Create New Project</button>' +
        '</div>';
}

async function assignEmail(emailId) {
    var select = document.getElementById('assignSelect');
    if (!select || !select.value) {
        showStatus('Select a project first', 'error');
        return;
    }

    try {
        await apiCallJson('/api/emails/' + emailId + '/assign', 'POST', { projectId: select.value });
        showStatus('Email assigned', 'success');
        await loadProjects();
        projectDetail.innerHTML = '<div class="detail-empty">Email assigned successfully</div>';
    } catch (err) {
        showStatus('Assign failed: ' + err.message, 'error');
    }
}

async function createProjectFromEmail(emailId) {
    try {
        const result = await apiCall('/api/emails/' + emailId + '/create-project', 'POST');
        showStatus('Project created: ' + result.projectName, 'success');
        await loadProjects();
        loadProjectDetail(result.projectId);
    } catch (err) {
        showStatus('Create failed: ' + err.message, 'error');
    }
}

// ---- Project detail ----

async function loadProjectDetail(projectId) {
    currentProjectId = projectId;

    document.querySelectorAll('.project-item').forEach(el => {
        el.classList.toggle('active', el.getAttribute('data-id') === projectId);
    });

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
            '<div class="detail-actions">' +
                '<button class="btn-generate" onclick="generateSummary(\'' + p.id + '\')">Generate Summary</button>' +
            '</div>' +
            '</div>';

        html += '<div id="summaryBlock"></div>';
        html += '<div id="actionItemsBlock"></div>';

        loadSummary(p.id);
        loadActionItems(p.id);

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

// ---- AI Summary ----

async function loadSummary(projectId) {
    try {
        const summary = await apiCall('/api/projects/' + projectId + '/summary');
        renderSummary(summary);
    } catch (err) {
        // No summary yet
    }
}

async function generateSummary(projectId) {
    const block = document.getElementById('summaryBlock');
    if (!block) return;

    block.innerHTML = '<div class="loading">Generating AI summary...</div>';
    showStatus('Generating summary...', '');

    try {
        const summary = await apiCall('/api/projects/' + projectId + '/summary', 'POST');
        renderSummary(summary);
        loadActionItems(projectId);
        showStatus('Summary generated', 'success');
    } catch (err) {
        block.innerHTML = '<div class="summary-card"><div class="summary-section"><div class="summary-label">Error</div><div class="summary-content">' + escapeHtml(err.message) + '</div></div></div>';
        showStatus('Summary failed: ' + err.message, 'error');
    }
}

function renderSummary(s) {
    const block = document.getElementById('summaryBlock');
    if (!block) return;

    block.innerHTML =
        '<div class="summary-card">' +
            '<div class="summary-title">AI Summary <span class="summary-date">' + formatDate(s.generatedAtUtc) + '</span></div>' +
            '<div class="summary-section">' +
                '<div class="summary-label">Summary</div>' +
                '<div class="summary-content">' + escapeHtml(s.summaryText) + '</div>' +
            '</div>' +
            '<div class="summary-section">' +
                '<div class="summary-label">Current Status</div>' +
                '<div class="summary-content">' + escapeHtml(s.currentStatus) + '</div>' +
            '</div>' +
            '<div class="summary-section">' +
                '<div class="summary-label">Pending Items</div>' +
                '<div class="summary-content summary-list">' + escapeHtml(s.pendingItems) + '</div>' +
            '</div>' +
            '<div class="summary-section">' +
                '<div class="summary-label">Suggested Next Action</div>' +
                '<div class="summary-content summary-action">' + escapeHtml(s.suggestedNextAction) + '</div>' +
            '</div>' +
        '</div>';
}

// ---- Action Items ----

async function loadActionItems(projectId) {
    const block = document.getElementById('actionItemsBlock');
    if (!block) return;

    try {
        const items = await apiCall('/api/projects/' + projectId + '/actions');
        renderActionItems(items, projectId);
    } catch (err) {
        // No action items yet
    }
}

function renderActionItems(items, projectId) {
    const block = document.getElementById('actionItemsBlock');
    if (!block) return;

    if (!items || items.length === 0) {
        block.innerHTML = '';
        return;
    }

    var html = '<div class="actions-card">' +
        '<div class="actions-title">Action Items (' + items.length + ')</div>';

    for (const a of items) {
        var statusCls = a.status === 'Done' ? 'action-done' : a.status === 'Dismissed' ? 'action-dismissed' : '';
        html += '<div class="action-item ' + statusCls + '">' +
            '<div class="action-item-text">' + escapeHtml(a.title) + '</div>' +
            '<div class="action-item-controls">';

        if (a.status === 'Pending') {
            html += '<button class="btn-action-done" onclick="markActionDone(\'' + projectId + '\',\'' + a.id + '\')">Done</button>' +
                '<button class="btn-action-dismiss" onclick="dismissAction(\'' + projectId + '\',\'' + a.id + '\')">Dismiss</button>';
        } else {
            html += '<span class="action-status-label">' + escapeHtml(a.status) + '</span>';
        }

        html += '</div></div>';
    }

    html += '</div>';
    block.innerHTML = html;
}

async function markActionDone(projectId, actionItemId) {
    try {
        await apiCall('/api/projects/' + projectId + '/actions/' + actionItemId + '/done', 'POST');
        loadActionItems(projectId);
        showStatus('Action item marked done', 'success');
    } catch (err) {
        showStatus('Failed: ' + err.message, 'error');
    }
}

async function dismissAction(projectId, actionItemId) {
    try {
        await apiCall('/api/projects/' + projectId + '/actions/' + actionItemId + '/dismiss', 'POST');
        loadActionItems(projectId);
        showStatus('Action item dismissed', 'success');
    } catch (err) {
        showStatus('Failed: ' + err.message, 'error');
    }
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
