// Dashboard interactivity

// KPI click -> toggle panel visibility
function openDashboardSection(section, scroll) {
    const panel = document.getElementById('panel-' + section);
    if (!panel) return false;

    // Hide all panels
    document.querySelectorAll('.panel').forEach(p => p.classList.remove('active'));
    // Remove active state from all KPIs
    document.querySelectorAll('.kpi[data-section]').forEach(k => k.classList.remove('kpi-active'));

    // Show target panel
    panel.classList.add('active');
    const kpi = document.querySelector('.kpi[data-section="' + section + '"]');
    if (kpi) kpi.classList.add('kpi-active');
    if (scroll !== false) panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    return true;
}

document.querySelectorAll('.kpi[data-section]').forEach(kpi => {
    kpi.addEventListener('click', () => {
        openDashboardSection(kpi.getAttribute('data-section'), true);
    });
});

// On load, restore the panel referenced by the URL hash (e.g. #panel-globalgroups),
// e.g. when returning from the Group Membership Tree page.
(function restorePanelFromHash() {
    const hash = window.location.hash;
    if (hash && hash.indexOf('#panel-') === 0) {
        openDashboardSection(hash.substring('#panel-'.length), true);
    }
})();

// Table sorting
function initTableSort() {
    document.querySelectorAll('.panel table').forEach(table => {
        const headers = table.querySelectorAll('thead th');
        headers.forEach((th, colIndex) => {
            // Skip columns with no text (e.g. manage/action icon columns) - these are not sortable or filterable
            if (!th.textContent.trim()) return;

            th.classList.add('sortable');

            // Clicking the header label sorts; the filter button (added below) handles filtering.
            th.addEventListener('click', (e) => {
                // Ignore clicks that originate on the filter button / dropdown
                if (e.target.closest('.col-filter')) return;

                const currentDir = th.getAttribute('data-sort-dir');
                const newDir = currentDir === 'asc' ? 'desc' : 'asc';

                // Clear sort state from all headers in this table
                headers.forEach(h => {
                    h.removeAttribute('data-sort-dir');
                    h.classList.remove('sort-asc', 'sort-desc');
                });

                th.setAttribute('data-sort-dir', newDir);
                th.classList.add(newDir === 'asc' ? 'sort-asc' : 'sort-desc');

                sortTable(table, colIndex, newDir);
            });

            initColumnFilter(table, th, colIndex);
        });
    });
}

function sortTable(table, colIndex, direction) {
    const tbody = table.querySelector('tbody');
    const rows = Array.from(tbody.querySelectorAll('tr:not(.empty-row-tr)'));

    // Skip if only empty-row placeholder
    if (rows.length === 0) return;

    // Filter out empty-row placeholders
    const dataRows = rows.filter(r => !r.querySelector('.empty-row'));

    dataRows.sort((a, b) => {
        const aCell = a.cells[colIndex];
        const bCell = b.cells[colIndex];
        if (!aCell || !bCell) return 0;

        const aText = (aCell.textContent || '').trim().toLowerCase();
        const bText = (bCell.textContent || '').trim().toLowerCase();

        // Try numeric comparison first
        const aNum = parseFloat(aText);
        const bNum = parseFloat(bText);
        if (!isNaN(aNum) && !isNaN(bNum)) {
            return direction === 'asc' ? aNum - bNum : bNum - aNum;
        }

        // String comparison
        if (aText < bText) return direction === 'asc' ? -1 : 1;
        if (aText > bText) return direction === 'asc' ? 1 : -1;
        return 0;
    });

    // Re-append sorted rows
    dataRows.forEach(row => tbody.appendChild(row));
}

// ---------------------------------------------------------------------------
// Excel-like per-column filtering
// ---------------------------------------------------------------------------
// Filter state is tracked per table on the DOM element itself:
//   table.__colFilters = { [colIndex]: Set(selectedValues) }
// A column is considered "unfiltered" when it has no entry (all values shown).

function getDataRows(table) {
    const tbody = table.querySelector('tbody');
    if (!tbody) return [];
    return Array.from(tbody.querySelectorAll('tr')).filter(r => !r.querySelector('.empty-row'));
}

function getColumnValues(table, colIndex) {
    const values = new Set();
    getDataRows(table).forEach(row => {
        const cell = row.cells[colIndex];
        if (cell) values.add((cell.textContent || '').trim());
    });
    return Array.from(values).sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
}

function applyFilters(table) {
    const filters = table.__colFilters || {};
    const activeCols = Object.keys(filters).map(Number);

    getDataRows(table).forEach(row => {
        let visible = true;
        for (const colIndex of activeCols) {
            const selected = filters[colIndex];
            const cell = row.cells[colIndex];
            const value = cell ? (cell.textContent || '').trim() : '';
            if (!selected.has(value)) { visible = false; break; }
        }
        row.style.display = visible ? '' : 'none';
    });

    // Toggle empty-state placeholder if all data rows are hidden by filters
    const tbody = table.querySelector('tbody');
    if (tbody) {
        const anyVisible = getDataRows(table).some(r => r.style.display !== 'none');
        let placeholder = tbody.querySelector('.filter-empty-row');
        if (!anyVisible) {
            if (!placeholder) {
                const colCount = table.querySelectorAll('thead th').length || 1;
                const tr = document.createElement('tr');
                tr.className = 'filter-empty-row empty-row-tr';
                tr.innerHTML = `<td colspan="${colCount}" class="empty-row">No rows match the current filters</td>`;
                tbody.appendChild(tr);
            }
        } else if (placeholder) {
            placeholder.remove();
        }
    }
}

function initColumnFilter(table, th, colIndex) {
    if (!table.__colFilters) table.__colFilters = {};

    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'col-filter';
    btn.title = 'Filter column';
    btn.setAttribute('aria-label', 'Filter column');
    btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></svg>';
    th.appendChild(btn);

    btn.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleFilterDropdown(table, th, colIndex, btn);
    });
}

function closeAllFilterDropdowns() {
    document.querySelectorAll('.col-filter-dropdown').forEach(d => d.remove());
    document.querySelectorAll('th .col-filter.open').forEach(b => b.classList.remove('open'));
}

function toggleFilterDropdown(table, th, colIndex, btn) {
    const alreadyOpen = btn.classList.contains('open');
    closeAllFilterDropdowns();
    if (alreadyOpen) return;

    btn.classList.add('open');

    const filters = table.__colFilters || {};
    const allValues = getColumnValues(table, colIndex);
    const selected = filters[colIndex] ? new Set(filters[colIndex]) : new Set(allValues);

    const dropdown = document.createElement('div');
    dropdown.className = 'col-filter-dropdown';
    dropdown.innerHTML = `
        <div class="cfd-search"><input type="text" placeholder="Search..." /></div>
        <label class="cfd-all"><input type="checkbox" class="cfd-select-all" /> (Select All)</label>
        <div class="cfd-list"></div>
        <div class="cfd-actions">
            <button type="button" class="cfd-apply">Apply</button>
            <button type="button" class="cfd-clear">Clear</button>
        </div>`;

    const list = dropdown.querySelector('.cfd-list');
    allValues.forEach(val => {
        const label = document.createElement('label');
        label.className = 'cfd-item';
        const display = val === '' ? '(Blanks)' : val;
        label.innerHTML = `<input type="checkbox" value="${val.replace(/"/g, '&quot;')}" ${selected.has(val) ? 'checked' : ''} /> <span></span>`;
        label.querySelector('span').textContent = display;
        list.appendChild(label);
    });

    const selectAll = dropdown.querySelector('.cfd-select-all');
    const syncSelectAll = () => {
        const boxes = Array.from(list.querySelectorAll('input[type=checkbox]'));
        const visibleBoxes = boxes.filter(b => b.closest('.cfd-item').style.display !== 'none');
        const checkedCount = visibleBoxes.filter(b => b.checked).length;
        selectAll.checked = checkedCount === visibleBoxes.length && visibleBoxes.length > 0;
        selectAll.indeterminate = checkedCount > 0 && checkedCount < visibleBoxes.length;
    };
    syncSelectAll();

    selectAll.addEventListener('change', () => {
        list.querySelectorAll('.cfd-item').forEach(item => {
            if (item.style.display === 'none') return;
            item.querySelector('input[type=checkbox]').checked = selectAll.checked;
        });
    });

    list.addEventListener('change', syncSelectAll);

    // Search box filters the checkbox list
    const search = dropdown.querySelector('.cfd-search input');
    search.addEventListener('input', () => {
        const term = search.value.trim().toLowerCase();
        list.querySelectorAll('.cfd-item').forEach(item => {
            const text = item.querySelector('span').textContent.toLowerCase();
            item.style.display = text.includes(term) ? '' : 'none';
        });
        syncSelectAll();
    });

    dropdown.querySelector('.cfd-apply').addEventListener('click', () => {
        const checked = Array.from(list.querySelectorAll('input[type=checkbox]:checked')).map(b => b.value);
        if (checked.length === allValues.length) {
            delete table.__colFilters[colIndex];
            th.classList.remove('filtered');
        } else {
            table.__colFilters[colIndex] = new Set(checked);
            th.classList.add('filtered');
        }
        applyFilters(table);
        closeAllFilterDropdowns();
    });

    dropdown.querySelector('.cfd-clear').addEventListener('click', () => {
        delete table.__colFilters[colIndex];
        th.classList.remove('filtered');
        applyFilters(table);
        closeAllFilterDropdowns();
    });

    // Prevent clicks inside the dropdown from bubbling to the header (which would sort/close)
    dropdown.addEventListener('click', (e) => e.stopPropagation());

    th.appendChild(dropdown);
    search.focus();
}

// Close open filter dropdowns when clicking elsewhere
document.addEventListener('click', (e) => {
    if (!e.target.closest('.col-filter-dropdown') && !e.target.closest('.col-filter')) {
        closeAllFilterDropdowns();
    }
});

// Initialize sorting on page load
initTableSort();

// Category charts (rendered via Chart.js, self-hosted)
// Maps the dashboard's CSS color names to hex values used for chart segments.
const CHART_COLORS = {
    blue: '#2563eb',
    green: '#16a34a',
    purple: '#7c3aed',
    teal: '#0d9488',
    amber: '#d97706',
    pink: '#db2777',
    slate: '#475569',
    red: '#dc2626',
    orange: '#ea580c',
    indigo: '#4f46e5'
};

// External HTML tooltip handler for charts. Renders the tooltip as a DOM element
// appended to <body> so it can overflow small canvases without being clipped.
function htmlTooltipHandler(context) {
    const { chart, tooltip } = context;
    let el = document.getElementById('chartjs-html-tooltip');
    if (!el) {
        el = document.createElement('div');
        el.id = 'chartjs-html-tooltip';
        el.className = 'chartjs-html-tooltip';
        document.body.appendChild(el);
    }

    if (tooltip.opacity === 0) {
        el.style.opacity = '0';
        return;
    }

    if (tooltip.body) {
        const lines = tooltip.body.map(b => b.lines).flat();
        const colors = tooltip.labelColors || [];
        el.innerHTML = lines.map((line, i) => {
            const bg = colors[i] ? colors[i].backgroundColor : 'transparent';
            return '<div class="tt-row"><span class="tt-swatch" style="background:' + bg + '"></span><span>' + line + '</span></div>';
        }).join('');
    }

    const rect = chart.canvas.getBoundingClientRect();
    el.style.left = rect.left + tooltip.caretX + 'px';
    el.style.top = rect.top + tooltip.caretY + 'px';
    el.style.opacity = '1';
}

// Custom Chart.js plugin: draws the percentage of the total centered on each
// pie/doughnut slice. Written inline so no external plugin download is required.
const sliceLabelsPlugin = {
    id: 'sliceLabels',
    afterDatasetsDraw(chart) {
        const { ctx } = chart;
        const meta = chart.getDatasetMeta(0);
        if (!meta || !meta.data) return;

        const dataset = chart.data.datasets[0];
        const total = dataset.data.reduce((sum, v) => sum + (Number(v) || 0), 0);
        if (total <= 0) return;

        ctx.save();
        ctx.font = '700 12px system-ui, -apple-system, Segoe UI, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        meta.data.forEach((arc, i) => {
            const value = Number(dataset.data[i]) || 0;
            if (value <= 0) return;

            const pct = Math.round((value / total) * 100);
            if (pct < 5) return; // skip labels on very thin slices to avoid clutter

            const pos = arc.tooltipPosition();
            ctx.fillStyle = '#ffffff';
            ctx.strokeStyle = 'rgba(0,0,0,.35)';
            ctx.lineWidth = 3;
            const text = pct + '%';
            ctx.strokeText(text, pos.x, pos.y);
            ctx.fillText(text, pos.x, pos.y);
        });

        ctx.restore();
    }
};

// Custom Chart.js plugin: gives vertical bars a pseudo 3-D appearance by drawing
// a shaded right face and a lighter top face behind/around each bar.
const bar3dPlugin = {
    id: 'bar3d',
    afterDatasetsDraw(chart) {
        const meta = chart.getDatasetMeta(0);
        if (!meta || !meta.data || meta.data.length === 0) return;
        const ctx = chart.ctx;
        const depth = 12;
        ctx.save();
        meta.data.forEach((bar, i) => {
            const color = chart.data.datasets[0].backgroundColor[i] || '#94a3b8';
            const { x, y, base, width } = bar.getProps(['x', 'y', 'base', 'width'], true);
            const half = width / 2;
            const left = x - half;
            const right = x + half;
            const top = Math.min(y, base);
            const bottom = Math.max(y, base);

            // Right (side) face - darker shade.
            ctx.fillStyle = shadeColor(color, -0.22);
            ctx.beginPath();
            ctx.moveTo(right, top);
            ctx.lineTo(right + depth, top - depth);
            ctx.lineTo(right + depth, bottom - depth);
            ctx.lineTo(right, bottom);
            ctx.closePath();
            ctx.fill();

            // Top face - lighter shade.
            ctx.fillStyle = shadeColor(color, 0.18);
            ctx.beginPath();
            ctx.moveTo(left, top);
            ctx.lineTo(left + depth, top - depth);
            ctx.lineTo(right + depth, top - depth);
            ctx.lineTo(right, top);
            ctx.closePath();
            ctx.fill();
        });
        ctx.restore();
    }
};

// Lightens (amount > 0) or darkens (amount < 0) a hex color. amount is -1..1.
function shadeColor(hex, amount) {
    let c = hex.replace('#', '');
    if (c.length === 3) c = c.split('').map(x => x + x).join('');
    const num = parseInt(c, 16);
    let r = (num >> 16) & 0xff, g = (num >> 8) & 0xff, b = num & 0xff;
    const t = amount < 0 ? 0 : 255;
    const p = Math.abs(amount);
    r = Math.round((t - r) * p) + r;
    g = Math.round((t - g) * p) + g;
    b = Math.round((t - b) * p) + b;
    return 'rgb(' + r + ',' + g + ',' + b + ')';
}

// Tracks live Chart.js instances per canvas so they can be destroyed on toggle.
const chartInstances = new WeakMap();

// Builds (or rebuilds) a Chart on the given canvas. mode is 'original' or 'bar3d'.
function buildChart(canvas, mode) {
    let labels, values, colorNames;
    try {
        labels = JSON.parse(canvas.getAttribute('data-chart-labels') || '[]');
        values = JSON.parse(canvas.getAttribute('data-chart-values') || '[]');
        colorNames = JSON.parse(canvas.getAttribute('data-chart-colors') || '[]');
    } catch (e) {
        return;
    }
    if (!values.length) return;

    const originalType = canvas.getAttribute('data-chart-type') || 'doughnut';
    const type = mode === 'bar3d' ? 'bar' : originalType;
    const backgroundColor = colorNames.map(c => CHART_COLORS[c] || '#94a3b8');
    const isCircular = type === 'doughnut' || type === 'pie';
    const isBar3d = mode === 'bar3d';
    const offset = parseInt(canvas.getAttribute('data-chart-offset') || '0', 10) || 0;
    const total = values.reduce((sum, v) => sum + v, 0);

    const existing = chartInstances.get(canvas);
    if (existing) { existing.destroy(); }

    // Mark bar mode so CSS can size the canvas like the doughnut (fixed height),
    // overriding any pie-specific compact sizing. Applies to 3-D toggle bars and
    // charts declared as bar (notoggle account-option column charts).
    canvas.classList.toggle('chart-bar3d', isBar3d || type === 'bar');

    const chart = new Chart(canvas, {
        type: type,
        data: {
            labels: labels,
            datasets: [{
                data: values,
                backgroundColor: backgroundColor,
                borderWidth: isCircular ? 1 : 0,
                borderColor: '#ffffff',
                offset: isCircular && offset > 0 ? offset : 0,
                hoverOffset: isCircular ? offset + 6 : 0,
                // Leave headroom so the 3-D top face isn't clipped at the top.
                maxBarThickness: 54,
                categoryPercentage: 0.7,
                barPercentage: 0.8
            }]
        },
        plugins: isCircular ? [sliceLabelsPlugin] : (isBar3d ? [bar3dPlugin] : []),
        options: {
            responsive: true,
            // Pies keep a true 1:1 ratio; doughnuts and 3-D bars fill the fixed-height container.
            maintainAspectRatio: type === 'pie',
            layout: isBar3d ? { padding: { top: 16, right: 16 } } : {},
            plugins: {
                legend: {
                    display: isCircular,
                    position: type === 'pie' ? 'bottom' : 'right',
                    labels: {
                        boxWidth: 12,
                        font: { size: 11 },
                        // Long source names (e.g. Entra tenant FQDNs) can overflow the legend
                        // box and get clipped. Build labels directly from data.labels and
                        // truncate the visible text with an ellipsis; the full name remains
                        // available in the slice tooltip.
                        generateLabels: function(chart) {
                            const data = chart.data;
                            const ds = data.datasets[0] || {};
                            const MAX = 22;
                            return (data.labels || []).map((label, i) => {
                                const text = String(label);
                                const bg = Array.isArray(ds.backgroundColor) ? ds.backgroundColor[i] : ds.backgroundColor;
                                return {
                                    text: text.length > MAX ? text.slice(0, MAX - 1) + '\u2026' : text,
                                    fillStyle: bg,
                                    strokeStyle: bg,
                                    lineWidth: 0,
                                    hidden: false,
                                    index: i
                                };
                            });
                        }
                    }
                },
                tooltip: {
                    enabled: !isCircular,
                    external: isCircular ? htmlTooltipHandler : undefined,
                    callbacks: {
                        label: function(context) {
                            const value = context.parsed.y !== undefined && !isCircular ? context.parsed.y : context.parsed;
                            const pct = total > 0 ? Math.round((value / total) * 100) : 0;
                            return context.label + ': ' + value.toLocaleString() + ' (' + pct + '%)';
                        }
                    }
                }
            },
            scales: isCircular ? {} : {
                x: { ticks: { font: { size: 10 } } },
                y: { beginAtZero: true, ticks: { precision: 0 } }
            }
        }
    });

    chartInstances.set(canvas, chart);
}

function initCategoryCharts() {
    // Chart.js may not be present (e.g. file not yet deployed) - fail gracefully.
    if (typeof Chart === 'undefined') {
        return;
    }

    document.querySelectorAll('canvas.dashboard-chart').forEach(canvas => {
        // Avoid double-initialization.
        if (canvas.dataset.chartInitialized === 'true') return;
        buildChart(canvas, 'original');
        canvas.dataset.chartInitialized = 'true';
    });
}

// Toggles all charts within a category chart area between their original
// (pie/doughnut) type and pseudo 3-D column charts.
function toggleCategoryChartType(btn) {
    if (typeof Chart === 'undefined') return;
    const wrap = btn.closest('.category-charts-wrap');
    if (!wrap) return;
    const newMode = btn.dataset.mode === 'bar3d' ? 'original' : 'bar3d';
    wrap.querySelectorAll('canvas.dashboard-chart').forEach(canvas => {
        // Charts flagged notoggle are locked to their declared type (e.g. account-option
        // column charts whose series are overlapping subsets, not a share-of-whole).
        if (canvas.getAttribute('data-chart-notoggle') === 'true') return;
        buildChart(canvas, newMode);
    });
    btn.dataset.mode = newMode;
    const title = newMode === 'bar3d' ? 'Switch to donut charts' : 'Switch to 3-D column charts';
    btn.title = title;
    btn.setAttribute('aria-label', title);
}


initCategoryCharts();

// Export modal
(function initExport() {
    const overlay = document.getElementById('exportModalOverlay');
    const openBtn = document.getElementById('btnExport');
    if (!overlay || !openBtn) return;

    const closeBtn = document.getElementById('exportModalClose');
    const cancelBtn = document.getElementById('exportCancel');
    const scopeSel = document.getElementById('exportScope');
    const subDashboardField = document.getElementById('exportSubDashboardField');
    const categoryField = document.getElementById('exportCategoryField');
    const kpiField = document.getElementById('exportKpiField');
    const includeChk = document.getElementById('exportIncludeDetails');
    const includeVal = document.getElementById('exportIncludeDetailsValue');
    const form = document.getElementById('exportForm');

    function open() { overlay.hidden = false; }
    function close() { overlay.hidden = true; }

    function updateScopeFields() {
        const scope = scopeSel.value;
        if (subDashboardField) subDashboardField.hidden = scope !== 'SubDashboard';
        categoryField.hidden = scope !== 'Category';
        kpiField.hidden = scope !== 'Kpi';
    }

    openBtn.addEventListener('click', open);
    if (closeBtn) closeBtn.addEventListener('click', close);
    if (cancelBtn) cancelBtn.addEventListener('click', close);
    overlay.addEventListener('click', e => { if (e.target === overlay) close(); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && !overlay.hidden) close(); });

    scopeSel.addEventListener('change', updateScopeFields);
    updateScopeFields();

    if (includeChk && includeVal) {
        includeChk.addEventListener('change', () => {
            includeVal.value = includeChk.checked ? 'true' : 'false';
        });
    }

    // Native form POST returns the file as an attachment; the browser handles the
    // download and the page does not navigate. There is no JS completion event for a file
    // download, so we show an "Exporting..." overlay on submit and hide it as soon as the
    // export finishes. Completion is detected primarily via a cookie the server sets on the
    // file response (echoing a unique token we send), which fires even when the browser
    // downloads directly without a Save dialog. Window focus and a timeout are fallbacks.
    if (form) {
        const tokenField = document.getElementById('exportDownloadToken');

        function getCookie(name) {
            const match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
            return match ? decodeURIComponent(match[1]) : null;
        }
        function clearCookie(name) {
            document.cookie = name + '=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
        }

        form.addEventListener('submit', () => {
            window.__isExporting = true;

            const overlay = document.getElementById('loadingOverlay');
            const message = overlay ? overlay.querySelector('p') : null;
            const originalMessage = message ? message.textContent : null;

            // Unique token for this download; the server echoes it back in the
            // "exportDownload" cookie once the file response is sent.
            const token = 'exp-' + Date.now() + '-' + Math.random().toString(36).slice(2);
            if (tokenField) tokenField.value = token;
            clearCookie('exportDownload');

            if (overlay) {
                if (message) message.textContent = form.getAttribute('data-exporting-text') || 'Exporting data...';
                overlay.classList.add('active');
            }

            close();

            let done = false;
            let pollId = null;
            const finish = () => {
                if (done) return;
                done = true;
                if (pollId) clearInterval(pollId);
                window.removeEventListener('focus', finish);
                clearCookie('exportDownload');
                if (overlay) {
                    overlay.classList.remove('active');
                    if (message && originalMessage !== null) message.textContent = originalMessage;
                }
                window.__isExporting = false;
            };

            // Primary: poll for the completion cookie set on the file response.
            pollId = setInterval(() => {
                if (getCookie('exportDownload') === token) finish();
            }, 250);

            // Fallback: the browser download often returns focus to the window.
            window.addEventListener('focus', () => { setTimeout(finish, 500); }, { once: true });

            // Last-resort fallback so the overlay never gets stuck.
            setTimeout(finish, 60000);
        });
    }
})();

// Segment filter (domain/tenant multi-select).
// - Two independent dropdowns (Domains, Tenants); each is a self-contained form.
// - No minimum selection: an empty selection means "none" and hides that source.
// - "Select all" / "Clear" set every checkbox; the selection is submitted when the
//   menu closes (outside click / toggle), so multiple toggles collapse into a single
//   postback. The server persists the selection and redirects back (?cached=true).
(function initSegmentFilters() {
    const forms = Array.from(document.querySelectorAll('[data-segment-form]'));
    if (forms.length === 0) return;

    forms.forEach(form => {
        const root = form.closest('[data-segment-filter]') || form;
        const toggle = form.querySelector('[data-segment-toggle]');
        const menu = form.querySelector('[data-segment-menu]');
        const selectAll = form.querySelector('[data-segment-all]');
        const selectNone = form.querySelector('[data-segment-none]');
        const checkboxes = Array.from(form.querySelectorAll('[data-segment-checkbox]'));
        if (!toggle || !menu) return;

        let dirty = false;

        function isOpen() {
            return !menu.hasAttribute('hidden');
        }

        function openMenu() {
            menu.removeAttribute('hidden');
            toggle.setAttribute('aria-expanded', 'true');
        }

        function closeMenu() {
            if (!isOpen()) return;
            menu.setAttribute('hidden', '');
            toggle.setAttribute('aria-expanded', 'false');
            if (dirty) {
                dirty = false;
                form.submit();
            }
        }

        toggle.addEventListener('click', e => {
            e.stopPropagation();
            if (isOpen()) { closeMenu(); } else { openMenu(); }
        });

        checkboxes.forEach(cb => {
            cb.addEventListener('change', () => { dirty = true; });
        });

        if (selectAll) {
            selectAll.addEventListener('click', () => {
                checkboxes.forEach(cb => {
                    if (!cb.checked) { cb.checked = true; dirty = true; }
                });
            });
        }

        if (selectNone) {
            selectNone.addEventListener('click', () => {
                checkboxes.forEach(cb => {
                    if (cb.checked) { cb.checked = false; dirty = true; }
                });
            });
        }

        document.addEventListener('click', e => {
            if (isOpen() && !form.contains(e.target)) closeMenu();
        });
        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && isOpen()) closeMenu();
        });
    });
})();

// --- Lazy, parallel Entra group membership loading ---
// The three membership-dependent Entra Groups KPI panels (Empty Groups, No Group Owner,
// Guest-Containing Groups) render a loading state on initial page load. This module fetches
// the membership data from the page's EntraMembership handler after render, populates each
// panel's table body, updates the matching KPI tile count, and shows a single completion toast.
(function initEntraMembershipLazyLoad() {
    var config = document.getElementById('entra-membership-config');
    if (!config) return;
    // Already loaded server-side (e.g. back-navigation with cached enriched data): nothing to do.
    if (config.getAttribute('data-loaded') === 'true') return;

    var endpoint = config.getAttribute('data-endpoint');
    var batchEndpoint = config.getAttribute('data-batch-endpoint');
    if (!endpoint && !batchEndpoint) return;
    var webUrl = (config.getAttribute('data-web-url') || '').replace(/\/+$/, '');
    // Localized strings emitted by _EntraMembershipConfig.cshtml. English literals are kept as
    // fallbacks so the loader still works if a page renders the config element without them.
    function loc(attr, fallback) {
        var v = config.getAttribute(attr);
        return (v !== null && v !== '') ? v : fallback;
    }
    var strings = {
        emptyEmptyGroups: loc('data-i18n-empty-emptygroups', 'No empty groups found'),
        emptyNoGroupOwner: loc('data-i18n-empty-nogroupowner', 'No groups without an owner found'),
        emptyGuestContaining: loc('data-i18n-empty-guestcontaining', 'No guest-containing groups found'),
        emptySingleOwner: loc('data-i18n-empty-singleowner', 'No single-owner groups found'),
        emptyLargeGroups: loc('data-i18n-empty-largegroups', 'No large groups found'),
        failedMembership: loc('data-i18n-failed-membership', 'Failed to load group membership.'),
        toastLoaded: loc('data-i18n-toast-loaded', 'Group membership loaded'),
        toastFailed: loc('data-i18n-toast-failed', 'Failed to load group membership'),
        toastLoading: loc('data-i18n-toast-loading', 'Loading group memberships. This may take a while depending on the environment. Group based KPIs may not be accurate and details will not be available until loading is complete.'),
        tipOpenWeb: loc('data-i18n-tip-openweb', 'Open in Web Interface'),
        tipConfigureWeb: loc('data-i18n-tip-configureweb', 'Configure Web Interface URL in Settings')
    };

    // Batched loading configuration (server-provided, admin-configurable).
    var totalGroups = parseInt(config.getAttribute('data-total-groups'), 10) || 0;
    var alreadyLoaded = parseInt(config.getAttribute('data-loaded-count'), 10) || 0;
    if (alreadyLoaded < 0) alreadyLoaded = 0;
    if (alreadyLoaded > totalGroups) alreadyLoaded = totalGroups;
    var remainingAtStart = Math.max(0, totalGroups - alreadyLoaded);
    var batchSize = parseInt(config.getAttribute('data-batch-size'), 10) || 40;
    if (batchSize < 1) batchSize = 1;
    var toastDelayMs = parseInt(config.getAttribute('data-toast-delay'), 10);
    if (isNaN(toastDelayMs) || toastDelayMs < 0) toastDelayMs = 500;

    // Maps the JSON payload keys to the panel/KPI section id suffix and an empty-state message.
    var kpiMap = {
        emptyGroups: { section: 'entraemptygroups', empty: strings.emptyEmptyGroups },
        noGroupOwner: { section: 'entranogroupowner', empty: strings.emptyNoGroupOwner },
        guestContaining: { section: 'entraguestcontaininggroups', empty: strings.emptyGuestContaining },
        singleOwner: { section: 'entrasingleownergroups', empty: strings.emptySingleOwner },
        largeGroups: { section: 'entralargegroups', empty: strings.emptyLargeGroups }
    };

    var linkSvg = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>';

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function webLinkCell(dn) {
        if (!dn) return '<td></td>';
        var safeDn = escapeHtml(dn);
        if (webUrl) {
            var href = webUrl + '/redirect.ashx?dn=' + encodeURIComponent(dn);
            return '<td><a href="javascript:void(0)" onclick="openWebInterface(\'' + href.replace(/'/g, "\\'") +
                '\')" title="' + strings.tipOpenWeb + '" class="btn-manage">' + linkSvg + '</a></td>';
        }
        return '<td><span class="btn-manage disabled" title="' + strings.tipConfigureWeb + '">' + linkSvg + '</span></td>';
    }

    function renderRows(kpiKey, payload) {
        var meta = kpiMap[kpiKey];
        var body = document.querySelector('[data-membership-body="' + kpiKey + '"]');
        var table = document.querySelector('[data-membership-table="' + kpiKey + '"]');
        var spinner = document.querySelector('[data-membership-spinner="' + kpiKey + '"]');
        var container = document.querySelector('[data-lazy-membership="' + kpiKey + '"]');
        if (spinner) spinner.style.display = 'none';

        if (payload && payload.error) {
            if (container) container.innerHTML = '<p class="muted">' + escapeHtml(payload.error) + '</p>';
        } else if (body) {
            var items = (payload && payload.items) || [];
            if (items.length === 0) {
                body.innerHTML = '<tr><td colspan="4" class="empty-row">' + escapeHtml(meta.empty) + '</td></tr>';
            } else {
                body.innerHTML = items.map(function (g) {
                    return '<tr><td>' + escapeHtml(g.name) + '</td><td>' + escapeHtml(g.tenant) +
                        '</td><td class="dn-cell">' + escapeHtml(g.dn) + '</td>' + webLinkCell(g.dn) + '</tr>';
                }).join('');
            }
            if (table) table.style.display = '';
        }

        // Update the matching KPI tile count. The same KPI can appear in multiple categories
        // (e.g. both "Groups" and "Governance and Risk"), each rendering a tile with the same
        // data-section, so update every occurrence - not just the first.
        if (payload && !payload.error) {
            document.querySelectorAll('.kpi[data-section="' + meta.section + '"] .val')
                .forEach(function (el) { el.textContent = payload.totalCount; });
        }
    }

    function showFailure() {
        Object.keys(kpiMap).forEach(function (kpiKey) {
            var spinner = document.querySelector('[data-membership-spinner="' + kpiKey + '"]');
            var container = document.querySelector('[data-lazy-membership="' + kpiKey + '"]');
            if (spinner) spinner.style.display = 'none';
            if (container) container.innerHTML = '<p class="muted">' + escapeHtml(strings.failedMembership) + '</p>';
        });
    }

    // --- Server-side collection in progress (user logged in mid-collection) ---------------
    // The shared superset collector is actively loading Entra group membership. The client must
    // NOT batch-load (the server owns loading); instead poll the progress endpoint, decrement the
    // badge to reflect the server's real progress, and reload once the server finishes so the
    // freshly-published superset (with membership) renders fully.
    var serverLoading = config.getAttribute('data-server-loading') === 'true';
    var progressEndpoint = config.getAttribute('data-progress-endpoint');
    if (serverLoading && progressEndpoint) {
        if (window.membershipBadge && remainingAtStart > 0) window.membershipBadge.set(remainingAtStart);
        if (window.showToast) window.showToast(strings.toastLoading, 'info');

        var pollProgress = function () {
            fetch(progressEndpoint, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (resp) {
                    if (!resp.ok) throw new Error('HTTP ' + resp.status);
                    return resp.json();
                })
                .then(function (data) {
                    var remaining = (typeof data.remaining === 'number') ? data.remaining : 0;
                    if (window.membershipBadge) window.membershipBadge.set(remaining);

                    // Server finished loading: reload to render the published superset membership.
                    if (data.done || !data.serverLoading) {
                        if (window.membershipBadge) window.membershipBadge.hide();
                        if (window.showToast) window.showToast(strings.toastLoaded, 'success');
                        window.location.reload();
                        return;
                    }
                    setTimeout(pollProgress, 2000);
                })
                .catch(function (err) {
                    // Stop polling on error; leave whatever is rendered in place.
                    console.error('Entra membership progress poll failed:', err);
                });
        };
        pollProgress();
        return;
    }

    // If the (single) batch endpoint isn't available, fall back to the original one-shot load.
    if (!batchEndpoint) {
        fetch(endpoint, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (resp) {
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                return resp.json();
            })
            .then(function (data) {
                renderRows('emptyGroups', data.emptyGroups);
                renderRows('noGroupOwner', data.noGroupOwner);
                renderRows('guestContaining', data.guestContaining);
                renderRows('singleOwner', data.singleOwner);
                renderRows('largeGroups', data.largeGroups);
                if (window.showToast) window.showToast(strings.toastLoaded, 'success');
            })
            .catch(function (err) {
                showFailure();
                if (window.showToast) window.showToast(strings.toastFailed, 'error');
                console.error('Entra membership lazy load failed:', err);
            });
        return;
    }

    // --- Batched loading: request groups in windows so the header badge can decrement. ---
    // A start toast is shown only if loading is still running after the configured delay, so
    // fast loads don't flash a transient message. The header badge (window.membershipBadge) is
    // initialized to the REMAINING group count and decremented after each completed batch. When
    // membership was already partly loaded in a previous page's session, loading resumes from
    // that offset instead of restarting from the full total.
    if (window.membershipBadge && remainingAtStart > 0) window.membershipBadge.set(remainingAtStart);

    // Nothing left to load (already fully loaded in session): keep the badge hidden and stop.
    if (totalGroups === 0 || remainingAtStart === 0) {
        if (window.membershipBadge) window.membershipBadge.hide();
        return;
    }

    var startToastShown = false;
    var startToastTimer = null;
    if (window.showToast && remainingAtStart > 0) {
        startToastTimer = setTimeout(function () {
            startToastShown = true;
            window.showToast(strings.toastLoading, 'info');
        }, toastDelayMs);
    }

    function cancelStartToast() {
        if (startToastTimer) { clearTimeout(startToastTimer); startToastTimer = null; }
    }

    function batchUrl(skip, take) {
        return batchEndpoint + (batchEndpoint.indexOf('?') >= 0 ? '&' : '?') +
            'skip=' + skip + '&take=' + take;
    }

    function loadBatch(skip) {
        fetch(batchUrl(skip, batchSize), { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (resp) {
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                return resp.json();
            })
            .then(function (data) {
                // Payloads are cumulative (recomputed from all groups loaded so far).
                renderRows('emptyGroups', data.emptyGroups);
                renderRows('noGroupOwner', data.noGroupOwner);
                renderRows('guestContaining', data.guestContaining);
                renderRows('singleOwner', data.singleOwner);
                renderRows('largeGroups', data.largeGroups);

                var total = (typeof data.totalGroups === 'number') ? data.totalGroups : totalGroups;
                var loaded = (typeof data.loadedCount === 'number') ? data.loadedCount : Math.min(total, skip + batchSize);
                var remaining = (typeof data.remaining === 'number') ? data.remaining : Math.max(0, total - loaded);
                if (window.membershipBadge) window.membershipBadge.set(remaining);

                if (data.done || remaining <= 0) {
                    cancelStartToast();
                    if (window.membershipBadge) window.membershipBadge.hide();
                    if (window.showToast) window.showToast(strings.toastLoaded, 'success');
                    return;
                }
                loadBatch(loaded);
            })
            .catch(function (err) {
                cancelStartToast();
                showFailure();
                if (window.membershipBadge) window.membershipBadge.hide();
                if (window.showToast) window.showToast(strings.toastFailed, 'error');
                console.error('Entra membership batch load failed:', err);
            });
    }

    loadBatch(alreadyLoaded);
})();


