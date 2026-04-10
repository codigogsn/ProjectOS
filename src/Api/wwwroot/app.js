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

function sanitizeHtml(raw) {
    // Sanitize HTML: keep safe tags, strip everything dangerous
    var text = raw;
    text = text.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '');
    text = text.replace(/<style[^>]*>[\s\S]*?<\/style>/gi, '');
    text = text.replace(/\son\w+="[^"]*"/gi, '');
    text = text.replace(/\son\w+='[^']*'/gi, '');
    text = text.replace(/<\/?(?:script|style|iframe|object|embed|form|input|button|select|textarea|meta|link|base)[^>]*>/gi, '');
    // Constrain images
    text = text.replace(/<img([^>]*)>/gi, function(m, attrs) {
        var src = (attrs.match(/src=["']([^"']+)["']/i) || [])[1];
        if (!src || /^(?:javascript|data:text)/i.test(src)) return '';
        return '<img src="' + escapeHtml(src) + '" loading="lazy" class="email-inline-img" />';
    });
    // Make links clickable and clean
    text = text.replace(/<a([^>]*)href=["']([^"']+)["']([^>]*)>([\s\S]*?)<\/a>/gi, function(m, pre, href, post, inner) {
        if (/utm_|click\.|track\.|redirect\.|unsubscribe/i.test(href)) return inner;
        var label = inner.replace(/<[^>]+>/g, '').trim();
        if (!label || label.length > 80) label = href.length > 50 ? href.substring(0, 50) + '...' : href;
        return '<a href="' + escapeHtml(href) + '" target="_blank" rel="noopener" class="email-inline-link">' + escapeHtml(label) + '</a>';
    });
    // Strip remaining unsafe tags but keep p, br, div, span, ul, ol, li, strong, em, h1-h6, table, tr, td, th, blockquote
    text = text.replace(/<\/?(?!(?:p|br|div|span|ul|ol|li|strong|em|b|i|h[1-6]|table|tr|td|th|blockquote|a|img)\b)[a-z][^>]*>/gi, '');
    return text;
}

function cleanEmailBody(raw) {
    if (!raw) return { main: '', forwarded: '', links: [], hasHtml: false };

    // Detect if content has real HTML
    var hasHtml = /<(?:p|div|table|br|h[1-6]|ul|ol)\b/i.test(raw);

    // Extract links from raw before any cleaning
    var linkMatches = raw.match(/https?:\/\/[^\s<"')\]]{10,}/g) || [];
    var links = [], seen = {};
    for (var i = 0; i < linkMatches.length; i++) {
        var u = linkMatches[i];
        if (u.length > 200 || /utm_|click\.|track\.|redirect\.|unsubscribe|manage.*preferences/i.test(u)) continue;
        if (!seen[u]) { seen[u] = true; links.push(u); }
    }

    // Detect forwarded section in raw
    var forwarded = '';
    var fwdPattern = /([-]{3,}\s*(?:Forwarded|Reenviad)[^\n]*[-]{3,}[\s\S]*)/i;

    if (hasHtml) {
        // HTML path: sanitize and keep structure
        var safe = sanitizeHtml(raw);
        // Remove tracking URLs inline
        safe = safe.replace(/https?:\/\/[^\s<"']{80,}/g, '');
        // Remove footer garbage
        safe = safe.replace(/(?:to\s+)?unsubscribe[^<\n]*/gi, '');
        safe = safe.replace(/manage\s+(?:your\s+)?preferences[^<\n]*/gi, '');
        safe = safe.replace(/view\s+(?:this\s+)?in\s+(?:your\s+)?browser[^<\n]*/gi, '');

        var fwdMatch = safe.match(fwdPattern);
        if (fwdMatch) { forwarded = fwdMatch[1]; safe = safe.substring(0, fwdMatch.index); }

        return { main: safe.trim(), forwarded: forwarded.trim(), links: links.slice(0, 5), hasHtml: true };
    }

    // Plain text path
    var text = raw;
    text = text.replace(/<style[^>]*>[\s\S]*?<\/style>/gi, '');
    text = text.replace(/<script[^>]*>[\s\S]*?<\/script>/gi, '');
    text = text.replace(/<br\s*\/?>/gi, '\n');
    text = text.replace(/<\/?(p|div|tr|li)[^>]*>/gi, '\n');
    text = text.replace(/<[^>]+>/g, '');
    text = text.replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&#\d+;/g, '');
    text = text.replace(/https?:\/\/[^\s]{80,}/g, '');
    text = text.replace(/https?:\/\/[^\s]*(?:utm_|click\.|track\.|redirect\.|unsubscribe)[^\s]*/gi, '');
    text = text.replace(/(?:to\s+)?unsubscribe[^\n]*\n?/gi, '');
    text = text.replace(/manage\s+(?:your\s+)?(?:email\s+)?preferences[^\n]*/gi, '');
    text = text.replace(/view\s+(?:this\s+)?(?:email\s+)?in\s+(?:your\s+)?browser[^\n]*/gi, '');
    text = text.replace(/you\s+(?:are\s+)?receiving\s+this[^\n]*/gi, '');
    text = text.replace(/if\s+you\s+no\s+longer\s+wish[^\n]*/gi, '');

    var fwdMatch = text.match(fwdPattern);
    if (fwdMatch) { forwarded = fwdMatch[1].trim(); text = text.substring(0, fwdMatch.index).trim(); }
    text = text.replace(/(^>.*\n?){5,}/gm, '> [...quoted text collapsed...]\n');
    text = text.replace(/[ \t]{3,}/g, ' ');
    text = text.replace(/\n{4,}/g, '\n\n');
    text = text.trim();
    if (forwarded) { forwarded = forwarded.replace(/https?:\/\/[^\s]{80,}/g, '').replace(/[ \t]{3,}/g, ' ').replace(/\n{4,}/g, '\n\n').trim(); }

    return { main: text, forwarded: forwarded, links: links.slice(0, 5), hasHtml: false };
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
        var cleaned = cleanEmailBody(e.body);

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

        // Parse variants
        var variants = null;
        try { if (e.aiReplyVariants) variants = JSON.parse(e.aiReplyVariants); } catch(ex) {}

        var replyIntent = e.aiReplyIntent || '';
        var intentBadge = '';
        if (replyIntent === 'no_reply') intentBadge = '<span class="intent-badge intent-no">No reply needed</span>';
        else if (replyIntent === 'optional') intentBadge = '<span class="intent-badge intent-optional">Reply optional</span>';

        // Reply editor
        html += '<div class="reply-editor-card">' +
            '<div class="reply-editor-header">' +
                '<span class="reply-editor-title">Reply ' + intentBadge + '</span>' +
                '<span class="reply-editor-to">To: ' + escapeHtml(e.fromEmail) + '</span>' +
            '</div>';

        if (variants && (variants.concise || variants.balanced || variants.warmer)) {
            html += '<div class="variant-chips">' +
                '<button class="variant-chip variant-recommended active" data-variant="balanced" onclick="selectVariant(this, \'balanced\')">Balanced <span class="variant-suggested">Suggested</span></button>' +
                '<button class="variant-chip" data-variant="concise" onclick="selectVariant(this, \'concise\')">Concise</button>' +
                '<button class="variant-chip" data-variant="warmer" onclick="selectVariant(this, \'warmer\')">Warmer</button>' +
                '<span id="variantData" style="display:none">' + escapeHtml(JSON.stringify(variants)) + '</span>' +
            '</div>';
            html += '<div class="reply-editor-wrap"><textarea class="reply-editor-textarea" id="replyEditor" rows="7">' + escapeHtml(variants.balanced || e.aiSuggestedReply || '') + '</textarea></div>';
        } else if (hasReply) {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="6">' + escapeHtml(e.aiSuggestedReply) + '</textarea>';
        } else if (hasSummary && replyIntent === 'no_reply') {
            html += '<div class="reply-not-needed"><span>This email does not require a reply.</span> <button class="btn-expand-editor" onclick="toggleNoReplyEditor(this)">Reply anyway</button></div>';
            html += '<div class="no-reply-editor-wrap" style="display:none"><textarea class="reply-editor-textarea" id="replyEditor" rows="3" placeholder="Write a reply..."></textarea></div>';
        } else if (hasSummary) {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="4" placeholder="No AI suggestion — write your reply here..."></textarea>';
        } else {
            html += '<textarea class="reply-editor-textarea" id="replyEditor" rows="4" placeholder="AI analysis pending..."></textarea>';
        }

        html += '<div class="reply-editor-actions">' +
            '<div class="reply-actions-left">' +
                '<button class="btn-reply-send" onclick="sendReply(\'' + e.id + '\')">Send</button>' +
                '<button class="btn-reply-draft" onclick="saveDraft(\'' + e.id + '\')">Save Draft</button>' +
            '</div>' +
            '<button class="btn-copy-reply" onclick="copyEditorText()"><span class="copy-icon">&#x2398;</span> Copy</button>' +
        '</div></div>';

        // Original email body — collapsible clean rendering
        var bodyContent = cleaned.hasHtml
            ? '<div class="email-body-clean email-html-body">' + cleaned.main + '</div>'
            : '<div class="email-body-clean">' + escapeHtml(cleaned.main || '(empty email)') + '</div>';

        html += '<div class="email-body-section">' +
            '<button class="email-body-toggle" onclick="toggleEmailBody(this)">Show original email</button>' +
            '<div class="email-body-collapsible" style="display:none">' + bodyContent;

        if (cleaned.forwarded) {
            html += '<div class="email-fwd-section">' +
                '<button class="email-fwd-toggle" onclick="toggleFwd(this)">Show forwarded content</button>' +
                '<div class="email-fwd-content" style="display:none">' + escapeHtml(cleaned.forwarded) + '</div>' +
            '</div>';
        }

        if (cleaned.links && cleaned.links.length > 0) {
            html += '<div class="email-links-section">' +
                '<button class="email-links-toggle" onclick="toggleLinks(this)">Show links (' + cleaned.links.length + ')</button>' +
                '<div class="email-links-list" style="display:none">';
            for (var li = 0; li < cleaned.links.length; li++) {
                html += '<a class="email-link-item" href="' + escapeHtml(cleaned.links[li]) + '" target="_blank" rel="noopener">' + escapeHtml(cleaned.links[li].length > 60 ? cleaned.links[li].substring(0, 60) + '...' : cleaned.links[li]) + '</a>';
            }
            html += '</div></div>';
        }

        html += '</div></div>';

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
    var btn = document.querySelector('.btn-reply-draft');
    if (btn) { btn.textContent = 'Saving...'; btn.disabled = true; }
    try {
        await apiCallJson('/api/emails/' + emailId + '/reply', 'POST', { body: editor.value, action: 'draft' });
        if (btn) { btn.textContent = 'Saved'; btn.classList.add('draft-saved'); }
        showStatus('Draft saved', 'success');
        setTimeout(function() { if (btn) { btn.textContent = 'Save Draft'; btn.disabled = false; btn.classList.remove('draft-saved'); } }, 2000);
        silentRefreshInbox();
    } catch (err) {
        showStatus('Save failed: ' + err.message, 'error');
        if (btn) { btn.textContent = 'Save Draft'; btn.disabled = false; }
    }
}

async function sendReply(emailId) {
    var editor = document.getElementById('replyEditor');
    if (!editor || !editor.value.trim()) { showStatus('Write a reply first', 'error'); return; }
    var btn = document.querySelector('.btn-reply-send');
    if (btn && btn.disabled) return; // prevent double-click
    if (btn) { btn.textContent = 'Sending...'; btn.disabled = true; }
    try {
        var result = await apiCallJson('/api/emails/' + emailId + '/reply', 'POST', { body: editor.value, action: 'send' });
        if (btn) { btn.textContent = 'Sent'; btn.classList.add('send-success'); }
        showStatus(result.message || 'Reply staged', 'success');
        setTimeout(function() { if (btn) { btn.textContent = 'Send'; btn.disabled = false; btn.classList.remove('send-success'); } }, 3000);
        silentRefreshInbox();
    } catch (err) {
        showStatus('Send failed: ' + err.message, 'error');
        if (btn) { btn.textContent = 'Send'; btn.disabled = false; }
    }
}

async function silentRefreshInbox() {
    // Refresh inbox list without losing selection or scroll
    var orgId = getOrgId();
    if (!orgId || currentView !== 'inbox') return;
    try {
        var emails = await apiCall('/api/emails?organizationId=' + orgId);
        var priOrder = { high: 0, medium: 1, low: 2 };
        emails.sort(function(a, b) {
            var pa = priOrder[a.aiPriority] !== undefined ? priOrder[a.aiPriority] : 3;
            var pb = priOrder[b.aiPriority] !== undefined ? priOrder[b.aiPriority] : 3;
            return pa !== pb ? pa - pb : new Date(b.sentAtUtc) - new Date(a.sentAtUtc);
        });
        var scroll = inboxList.scrollTop;
        var highCount = emails.filter(function(e) { return e.aiPriority === 'high'; }).length;
        var headerExtra = highCount > 0 ? ' <span class="inbox-header-urgent">' + highCount + ' urgent</span>' : '';
        inboxList.innerHTML = '<div class="inbox-list-header">Inbox (' + emails.length + ')' + headerExtra + '</div>';
        for (var e of emails) {
            var pri = (e.aiPriority || '').toLowerCase();
            var row = document.createElement('div');
            row.className = 'inbox-row' + (pri === 'high' ? ' inbox-row-high' : '') + (pri === 'low' ? ' inbox-row-low' : '') + (e.id === currentEmailId ? ' active' : '');
            row.setAttribute('data-id', e.id);
            row.onclick = (function(id) { return function() { loadEmailDetail(id); }; })(e.id);
            var badges = '';
            if (e.aiCategory && e.aiCategory !== 'unknown') badges += '<span class="ai-cat-badge cat-' + escapeHtml(e.aiCategory) + '">' + escapeHtml(e.aiCategory) + '</span>';
            if (pri === 'high') badges += '<span class="ai-pri-badge pri-high">urgent</span>';
            var preview = (e.aiSummary && e.aiSummary !== 'Pending' && e.aiSummary !== 'AI unavailable') ? e.aiSummary : (e.bodyPreview || 'AI analysis pending...');
            row.innerHTML = '<div class="inbox-row-top"><div class="inbox-row-subject">' + escapeHtml(e.subject || '(no subject)') + '</div><div class="inbox-row-date">' + formatRelative(e.sentAtUtc) + '</div></div>' +
                '<div class="inbox-row-mid"><span class="inbox-row-from">' + escapeHtml(e.fromEmail) + '</span>' + (badges ? '<span class="inbox-row-badges">' + badges + '</span>' : '') + '</div>' +
                '<div class="inbox-row-preview">' + escapeHtml(preview) + '</div>';
            inboxList.appendChild(row);
        }
        inboxList.scrollTop = scroll;
    } catch(ex) {}
}

function selectVariant(btn, type) {
    document.querySelectorAll('.variant-chip').forEach(function(c) { c.classList.remove('active'); });
    btn.classList.add('active');
    var dataEl = document.getElementById('variantData');
    if (!dataEl) return;
    try {
        var v = JSON.parse(dataEl.textContent);
        var editor = document.getElementById('replyEditor');
        if (!editor) return;
        // Smooth fade transition
        editor.style.opacity = '0';
        setTimeout(function() {
            editor.value = v[type] || '';
            editor.style.opacity = '1';
        }, 120);
    } catch(ex) {}
}

function copyEditorText() {
    var editor = document.getElementById('replyEditor');
    if (!editor) return;
    navigator.clipboard.writeText(editor.value).then(function() {
        showStatus('Reply copied', 'success');
    });
}

function toggleNoReplyEditor(btn) {
    var wrap = btn.closest('.reply-not-needed').nextElementSibling;
    if (wrap) { wrap.style.display = ''; btn.style.display = 'none'; }
}

function toggleEmailBody(btn) {
    var content = btn.nextElementSibling;
    if (content.style.display === 'none') { content.style.display = ''; btn.textContent = 'Hide original email'; }
    else { content.style.display = 'none'; btn.textContent = 'Show original email'; }
}
function toggleFwd(btn) {
    var content = btn.nextElementSibling;
    if (content.style.display === 'none') { content.style.display = ''; btn.textContent = 'Hide forwarded content'; }
    else { content.style.display = 'none'; btn.textContent = 'Show forwarded content'; }
}
function toggleLinks(btn) {
    var content = btn.nextElementSibling;
    if (content.style.display === 'none') { content.style.display = ''; btn.textContent = 'Hide links'; }
    else { content.style.display = 'none'; btn.textContent = 'Show links'; }
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
