const statusBar = document.getElementById('statusBar');
const projectList = document.getElementById('projectList');
const unassignedList = document.getElementById('unassignedList');
const projectDetail = document.getElementById('projectDetail');
const inboxView = document.getElementById('inboxView');
const inboxList = document.getElementById('inboxList');
const inboxDetail = document.getElementById('inboxDetail');
const sidebarProjects = document.getElementById('sidebarProjects');

let currentProjectId = null;
let cachedProjects = [];
let currentView = 'projects';
let currentEmailId = null;
const styleView = document.getElementById('styleView');

// ---- View switching ----

function switchView(view) {
    currentView = view;
    document.querySelectorAll('.nav-tab').forEach(t => t.classList.toggle('active', t.dataset.view === view));

    sidebarProjects.style.display = 'none';
    projectDetail.style.display = 'none';
    inboxView.style.display = 'none';
    styleView.style.display = 'none';

    if (view === 'projects') {
        sidebarProjects.style.display = '';
        projectDetail.style.display = '';
    } else if (view === 'inbox') {
        inboxView.style.display = '';
        loadInbox();
    } else if (view === 'style') {
        styleView.style.display = '';
        loadToneProfile();
    }
}

function loadCurrentView() {
    if (currentView === 'inbox') loadInbox();
    else if (currentView === 'style') loadToneProfile();
    else loadProjects();
}

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
        const result = await apiCall('/api/gmail/sync?organizationId=' + orgId);
        showStatus('Sync: ' + (result.saved || 0) + ' new, ' + (result.duplicates || 0) + ' duplicates', 'success');

        // Force refresh whichever view is active
        if (currentView === 'inbox') {
            await loadInbox();
            if (currentEmailId) await loadEmailDetail(currentEmailId);
        } else {
            await loadProjects();
        }
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

// ---- Inbox ----

async function loadInbox() {
    const orgId = getOrgId();
    if (!orgId) return;

    // Show skeleton loading
    var skeletonHtml = '<div class="inbox-list-header">Inbox</div>';
    for (var i = 0; i < 8; i++) {
        skeletonHtml += '<div class="skeleton-row"><div class="skeleton skeleton-line skeleton-line-medium"></div><div class="skeleton skeleton-line skeleton-line-short"></div><div class="skeleton skeleton-line skeleton-line-full"></div></div>';
    }
    inboxList.innerHTML = skeletonHtml;
    inboxDetail.innerHTML = '<div class="detail-empty-state"><div class="detail-empty-icon">&#9993;</div><div class="detail-empty-text">Select an email to view intelligence</div></div>';

    try {
        var emails = await apiCall('/api/emails?organizationId=' + orgId);

        // Sort: high → medium → low → null
        var priOrder = { high: 0, medium: 1, low: 2 };
        emails.sort(function(a, b) {
            var pa = priOrder[a.aiPriority] !== undefined ? priOrder[a.aiPriority] : 3;
            var pb = priOrder[b.aiPriority] !== undefined ? priOrder[b.aiPriority] : 3;
            if (pa !== pb) return pa - pb;
            return new Date(b.sentAtUtc) - new Date(a.sentAtUtc);
        });

        var highCount = emails.filter(function(e) { return e.aiPriority === 'high'; }).length;
        var headerExtra = highCount > 0 ? ' <span class="inbox-header-urgent">' + highCount + ' urgent</span>' : '';
        inboxList.innerHTML = '<div class="inbox-list-header">Inbox (' + emails.length + ')' + headerExtra + '</div>';

        if (emails.length === 0) {
            inboxList.insertAdjacentHTML('beforeend', '<div class="sidebar-empty">No emails yet. Click <strong>Sync Emails</strong> to load.</div>');
            return;
        }

        for (var e of emails) {
            var pri = (e.aiPriority || '').toLowerCase();
            var row = document.createElement('div');
            row.className = 'inbox-row' + (pri === 'high' ? ' inbox-row-high' : '') + (pri === 'low' ? ' inbox-row-low' : '');
            row.setAttribute('data-id', e.id);
            row.onclick = (function(emailId) { return function() { loadEmailDetail(emailId); }; })(e.id);

            var badges = '';
            if (e.aiCategory && e.aiCategory !== 'unknown') {
                badges += '<span class="ai-cat-badge cat-' + escapeHtml(e.aiCategory) + '">' + escapeHtml(e.aiCategory) + '</span>';
            }
            if (pri === 'high') {
                badges += '<span class="ai-pri-badge pri-high">urgent</span>';
            }

            var preview = '';
            if (e.aiSummary && e.aiSummary !== 'Pending' && e.aiSummary !== 'AI unavailable' && e.aiSummary !== 'AI processing failed') {
                preview = e.aiSummary;
            } else if (e.bodyPreview) {
                preview = e.bodyPreview;
            } else {
                preview = 'AI analysis pending...';
            }

            row.innerHTML =
                '<div class="inbox-row-top">' +
                    '<div class="inbox-row-subject">' + escapeHtml(e.subject || '(no subject)') + '</div>' +
                    '<div class="inbox-row-date">' + formatRelative(e.sentAtUtc) + '</div>' +
                '</div>' +
                '<div class="inbox-row-mid">' +
                    '<span class="inbox-row-from">' + escapeHtml(e.fromEmail) + '</span>' +
                    (badges ? '<span class="inbox-row-badges">' + badges + '</span>' : '') +
                '</div>' +
                '<div class="inbox-row-preview">' + escapeHtml(preview) + '</div>';
            inboxList.appendChild(row);
        }

        showStatus('Loaded ' + emails.length + ' emails', 'success');
    } catch (err) {
        inboxList.innerHTML = '<div class="inbox-list-header">Inbox</div><div class="sidebar-empty">Failed to load emails</div>';
        showStatus('Error: ' + err.message, 'error');
    }
}

function cleanEmailBody(raw) {
    if (!raw) return '';
    var text = raw;
    // Strip tracking URLs (long encoded URLs)
    text = text.replace(/https?:\/\/[^\s]{120,}/g, '[link removed]');
    // Collapse repeated forward markers
    text = text.replace(/([-]{3,}\s*Forwarded message\s*[-]{3,}\s*){2,}/gi, '--- Forwarded message ---\n');
    text = text.replace(/(>{2,}\s*\n?){3,}/g, '>>>\n');
    // Strip HTML tags if present in plain text
    text = text.replace(/<style[^>]*>[\s\S]*?<\/style>/gi, '');
    text = text.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '');
    text = text.replace(/<[^>]+>/g, ' ');
    // Decode common HTML entities
    text = text.replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
    // Collapse excessive whitespace
    text = text.replace(/[ \t]{4,}/g, '  ');
    text = text.replace(/\n{4,}/g, '\n\n\n');
    // Trim leading/trailing
    text = text.trim();
    return text;
}

async function loadEmailDetail(emailId) {
    currentEmailId = emailId;
    document.querySelectorAll('.inbox-row').forEach(r => r.classList.toggle('active', r.getAttribute('data-id') === emailId));

    inboxDetail.innerHTML = '<div class="loading">Loading email...</div>';

    try {
        var e = await apiCall('/api/emails/' + emailId);

        var catBadge = e.aiCategory && e.aiCategory !== 'unknown'
            ? '<span class="ai-cat-badge cat-' + escapeHtml(e.aiCategory) + '">' + escapeHtml(e.aiCategory) + '</span>' : '';
        var priBadge = e.aiPriority
            ? '<span class="ai-pri-badge pri-' + escapeHtml(e.aiPriority) + '">' + escapeHtml(e.aiPriority) + '</span>' : '';

        var hasSummary = e.aiSummary && e.aiSummary !== 'Pending' && e.aiSummary !== 'AI unavailable' && e.aiSummary !== 'AI processing failed';
        var hasReply = e.aiSuggestedReply && e.aiSuggestedReply.length > 0;
        var hasAi = hasSummary || hasReply;
        var cleanBody = cleanEmailBody(e.body);

        var html = '<div class="detail-fadein">';

        // Header
        html += '<div class="inbox-detail-top">' +
            '<div class="inbox-detail-subject">' + escapeHtml(e.subject || '(no subject)') + '</div>' +
            '<div class="inbox-detail-badges">' + catBadge + priBadge + '</div>' +
        '</div>' +
        '<div class="inbox-detail-meta">' +
            '<div class="inbox-detail-meta-row"><span class="inbox-meta-label">From</span><span class="inbox-meta-value">' + escapeHtml(e.fromEmail) + '</span></div>' +
            '<div class="inbox-detail-meta-row"><span class="inbox-meta-label">To</span><span class="inbox-meta-value">' + escapeHtml(e.toAddress) + '</span></div>' +
            '<div class="inbox-detail-meta-row"><span class="inbox-meta-label">Date</span><span class="inbox-meta-value">' + formatDate(e.sentAtUtc) + '</span></div>' +
        '</div>';

        // AI Intelligence card
        html += '<div class="ai-intel-card">' +
            '<div class="ai-intel-header"><span>AI Intelligence</span>' +
                '<span class="ai-intel-header-actions">' +
                    '<button class="btn-reprocess-single" onclick="reprocessSingleEmail(\'' + e.id + '\')">Reprocess</button>' +
                    (hasAi ? '<span class="ai-intel-status ai-ready">Ready</span>' : '<span class="ai-intel-status ai-pending">Pending</span>') +
                '</span>' +
            '</div>';

        // Summary
        html += '<div class="ai-intel-row">' +
            '<div class="ai-intel-label">Summary</div>' +
            '<div class="ai-intel-value">' +
                (hasSummary ? escapeHtml(e.aiSummary) : '<span class="ai-intel-empty">AI analysis pending...</span>') +
            '</div>' +
        '</div></div>';

        // Reply editor
        html += '<div class="reply-editor-card">' +
            '<div class="reply-editor-header">' +
                '<span class="reply-editor-title">Reply</span>' +
                '<span class="reply-editor-to">To: ' + escapeHtml(e.fromEmail) + '</span>' +
            '</div>';

        if (hasReply) {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="6">' + escapeHtml(e.aiSuggestedReply) + '</textarea>';
        } else if (hasSummary) {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="4" placeholder="No AI suggestion — write your reply here..."></textarea>';
        } else {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="4" placeholder="AI analysis pending... You can still write manually."></textarea>';
        }

        html += '<div class="reply-editor-actions">' +
            '<div class="reply-actions-left">' +
                '<button class="btn-reply-send" onclick="sendReply(\'' + e.id + '\')">Send</button>' +
                '<button class="btn-reply-draft" onclick="saveDraft(\'' + e.id + '\')">Save Draft</button>' +
            '</div>' +
            '<button class="btn-copy-reply" onclick="copyEditorText()"><span class="copy-icon">&#x2398;</span> Copy</button>' +
        '</div></div>';

        // Original email body
        html += '<div class="inbox-detail-section">' +
            '<div class="inbox-detail-section-title">Original Email</div>' +
            '<div class="inbox-detail-body email-body-clean">' + escapeHtml(cleanBody) + '</div>' +
        '</div>';

        html += '</div>';
        inboxDetail.innerHTML = html;
    } catch (err) {
        inboxDetail.innerHTML = '<div class="detail-empty-state"><div class="detail-empty-text">Failed to load email</div></div>';
        showStatus('Error: ' + err.message, 'error');
    }
}

async function saveDraft(emailId) {
    var editor = document.getElementById('replyEditor');
    if (!editor) return;
    try {
        await apiCallJson('/api/emails/' + emailId + '/reply', 'POST', { body: editor.value, action: 'draft' });
        showStatus('Draft saved', 'success');
    } catch (err) {
        showStatus('Save failed: ' + err.message, 'error');
    }
}

async function sendReply(emailId) {
    var editor = document.getElementById('replyEditor');
    if (!editor || !editor.value.trim()) { showStatus('Write a reply first', 'error'); return; }
    var btn = document.querySelector('.btn-reply-send');
    if (btn) { btn.textContent = 'Sending...'; btn.disabled = true; }
    try {
        var result = await apiCallJson('/api/emails/' + emailId + '/reply', 'POST', { body: editor.value, action: 'send' });
        showStatus(result.message || 'Reply sent', 'success');
    } catch (err) {
        showStatus('Send failed: ' + err.message, 'error');
    } finally {
        if (btn) { btn.textContent = 'Send'; btn.disabled = false; }
    }
}

function copyEditorText() {
    var editor = document.getElementById('replyEditor');
    if (!editor) return;
    navigator.clipboard.writeText(editor.value).then(function() {
        showStatus('Reply copied', 'success');
    });
}

// ---- AI Reprocessing ----

async function reprocessAi() {
    var orgId = getOrgId();
    if (!orgId) return;

    var btn = document.querySelector('.btn-reprocess');
    var origText = btn.textContent;
    btn.textContent = 'Reprocessing...';
    btn.disabled = true;

    try {
        var result = await apiCall('/api/emails/backfill-ai?organizationId=' + orgId + '&force=true&limit=50', 'POST');
        showStatus('AI reprocessed: ' + result.updated + ' updated, ' + result.failed + ' failed', 'success');
        if (currentView === 'inbox') {
            await loadInbox();
            if (currentEmailId) await loadEmailDetail(currentEmailId);
        }
    } catch (err) {
        showStatus('Reprocess failed: ' + err.message, 'error');
    } finally {
        btn.textContent = origText;
        btn.disabled = false;
    }
}

async function reprocessSingleEmail(emailId) {
    var orgId = getOrgId();
    if (!orgId) return;

    var btn = document.querySelector('.btn-reprocess-single');
    if (btn) { btn.textContent = 'Processing...'; btn.disabled = true; }

    try {
        await apiCall('/api/emails/backfill-ai?organizationId=' + orgId + '&emailId=' + emailId + '&force=true', 'POST');
        showStatus('Email reprocessed', 'success');
        await loadEmailDetail(emailId);
    } catch (err) {
        showStatus('Reprocess failed: ' + err.message, 'error');
        if (btn) { btn.textContent = 'Reprocess'; btn.disabled = false; }
    }
}

// ---- Tone Profile ----

async function loadToneProfile() {
    var orgId = getOrgId();
    if (!orgId) return;

    try {
        var p = await apiCall('/api/tone-profile?organizationId=' + orgId);
        document.getElementById('toneFormality').value = p.formality || 'professional';
        document.getElementById('toneLength').value = p.responseLength || 'medium';
        document.getElementById('toneAddress').value = p.addressStyle || 'neutral';
        document.getElementById('tonePrimaryTraits').value = p.primaryTraits || '';
        document.getElementById('toneAvoidTraits').value = p.avoidTraits || '';
        document.getElementById('toneUpset').value = p.upsetStyle || 'empathetic';
        document.getElementById('toneSales').value = p.salesStyle || 'consultative';
        document.getElementById('toneSignature').value = p.signature || '';
        document.getElementById('toneExample1').value = p.example1 || '';
        document.getElementById('toneExample2').value = p.example2 || '';
        updateStrength();
        updatePreview();
    } catch (err) {
        showStatus('Failed to load tone profile', 'error');
    }
}

var presets = {
    professional: { formality:'professional', length:'medium', address:'neutral', traits:'clear, precise, reliable', avoid:'robotic, vague', upset:'empathetic', sales:'consultative' },
    warm:         { formality:'warm', length:'medium', address:'neutral', traits:'warm, caring, consultative, thoughtful', avoid:'cold, robotic, pushy', upset:'empathetic', sales:'consultative' },
    direct:       { formality:'professional', length:'short', address:'neutral', traits:'direct, efficient, action-oriented', avoid:'vague, wordy, passive', upset:'direct', sales:'aggressive' },
    friendly:     { formality:'casual', length:'medium', address:'tu', traits:'friendly, helpful, approachable, positive', avoid:'formal, stiff, distant', upset:'empathetic', sales:'subtle' }
};

function applyPreset(name) {
    var p = presets[name]; if (!p) return;
    document.getElementById('toneFormality').value = p.formality;
    document.getElementById('toneLength').value = p.length;
    document.getElementById('toneAddress').value = p.address;
    document.getElementById('tonePrimaryTraits').value = p.traits;
    document.getElementById('toneAvoidTraits').value = p.avoid;
    document.getElementById('toneUpset').value = p.upset;
    document.getElementById('toneSales').value = p.sales;
    updateStrength();
    updatePreview();
    showStatus('Preset applied — review and save when ready', 'success');
}

function updateStrength() {
    var traits = (document.getElementById('tonePrimaryTraits').value || '').trim();
    var sig = (document.getElementById('toneSignature').value || '').trim();
    var ex1 = (document.getElementById('toneExample1').value || '').trim();
    var ex2 = (document.getElementById('toneExample2').value || '').trim();

    var level = 'Basic';
    var cls = 'strength-basic';
    if (traits.length > 3 && sig.length > 2) { level = 'Good'; cls = 'strength-good'; }
    if (traits.length > 3 && sig.length > 2 && (ex1.length > 20 || ex2.length > 20)) { level = 'Strong'; cls = 'strength-strong'; }

    var el = document.getElementById('profileStrength');
    if (el) el.innerHTML = '<span class="strength-dot ' + cls + '"></span> <span class="strength-label">' + level + '</span>';
}

function updatePreview() {
    var formality = document.getElementById('toneFormality').value;
    var length = document.getElementById('toneLength').value;
    var address = document.getElementById('toneAddress').value;
    var sig = document.getElementById('toneSignature').value || '';

    var greeting = { tu: 'Hola,', usted: 'Estimado/a,', neutral: 'Hola,' }[address] || 'Hola,';
    var body = '';
    if (formality === 'formal') body = 'Le informo que la propuesta actualizada con los nuevos precios ha sido preparada. Se la enviaré a la brevedad para su revisión.';
    else if (formality === 'warm') body = 'Claro que sí, con mucho gusto te preparo la propuesta actualizada. Dame un momento y te la envío hoy mismo.';
    else if (formality === 'casual') body = 'Dale, te mando los precios actualizados hoy sin falta.';
    else body = 'Gracias por el seguimiento. Preparo la propuesta actualizada y te la envío hoy.';

    if (length === 'short') body = body.split('.')[0] + '.';
    if (length === 'long') body += ' Si necesitas algo adicional o quieres que revisemos algún punto en particular, quedo a tu disposición.';

    var closing = sig ? '\n\n' + sig : '';
    var preview = greeting + '\n\n' + body + closing;

    var el = document.getElementById('voicePreviewText');
    if (el) el.textContent = preview;
}

async function saveToneProfile() {
    var orgId = getOrgId();
    if (!orgId) return;

    var status = document.getElementById('styleSaveStatus');
    status.textContent = 'Saving...';
    status.className = 'style-save-status';

    try {
        await apiCallJson('/api/tone-profile', 'POST', {
            organizationId: orgId,
            formality: document.getElementById('toneFormality').value,
            responseLength: document.getElementById('toneLength').value,
            addressStyle: document.getElementById('toneAddress').value,
            primaryTraits: document.getElementById('tonePrimaryTraits').value,
            avoidTraits: document.getElementById('toneAvoidTraits').value,
            upsetStyle: document.getElementById('toneUpset').value,
            salesStyle: document.getElementById('toneSales').value,
            signature: document.getElementById('toneSignature').value,
            example1: document.getElementById('toneExample1').value,
            example2: document.getElementById('toneExample2').value
        });
        status.textContent = 'Saved';
        status.className = 'style-save-status saved';
        setTimeout(function() { status.textContent = ''; }, 3000);
        showStatus('Tone profile saved', 'success');
    } catch (err) {
        status.textContent = 'Failed';
        status.className = 'style-save-status failed';
        showStatus('Save failed: ' + err.message, 'error');
    }
}

function copyReply(btn) {
    var text = btn.previousElementSibling.textContent;
    navigator.clipboard.writeText(text).then(function() {
        btn.textContent = 'Copied';
        btn.classList.add('copied');
        setTimeout(function() { btn.textContent = 'Copy'; btn.classList.remove('copied'); }, 2000);
    });
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
