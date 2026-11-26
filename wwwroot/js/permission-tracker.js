// Permission Tracker: busy indicator for filter/view/tab navigation and
// Excel-like sort + per-column filtering for the result tables (same UX as the drilldown tables).
(function () {
    'use strict';

    // ---------- busy overlay ----------
    var overlay = document.getElementById('ptBusy');
    function busy() {
        if (overlay) overlay.hidden = false;
        document.body.setAttribute('aria-busy', 'true');
        document.querySelectorAll('.pt-filters button, .pt-filters input, .pt-filters select').forEach(function (el) {
            // Disable after submit has captured values (next tick) so the form still posts them.
            setTimeout(function () { el.disabled = true; }, 0);
        });
    }
    // Restore when navigating back via bfcache.
    window.addEventListener('pageshow', function () {
        if (overlay) overlay.hidden = true;
        document.body.removeAttribute('aria-busy');
        document.querySelectorAll('.pt-filters button, .pt-filters input, .pt-filters select').forEach(function (el) { el.disabled = false; });
    });

    var filterForm = document.querySelector('form.pt-filters');
    if (filterForm) {
        filterForm.addEventListener('submit', busy);
        // Apply immediately when the view or a flag changes.
        filterForm.querySelectorAll('select, input[type=checkbox]').forEach(function (el) {
            el.addEventListener('change', function () {
                if (filterForm.requestSubmit) filterForm.requestSubmit(); else { busy(); filterForm.submit(); }
            });
        });
    }
    document.querySelectorAll('.pt-tabs a').forEach(function (a) { a.addEventListener('click', busy); });

    // ---------- table sort + column filter ----------
    function text(cell) { return cell ? (cell.textContent || '').trim() : ''; }
    function dataRows(table) {
        return Array.from(table.tBodies[0] ? table.tBodies[0].rows : []).filter(function (r) { return !r.classList.contains('pt-empty-row'); });
    }

    function applyFilters(table) {
        var filters = table.__ptFilters;
        var cols = Object.keys(filters);
        var any = false;
        dataRows(table).forEach(function (row) {
            var visible = cols.every(function (c) { return filters[c].has(text(row.cells[c])); });
            row.style.display = visible ? '' : 'none';
            if (visible) any = true;
        });
        var tbody = table.tBodies[0];
        var empty = tbody.querySelector('.pt-empty-row');
        if (!any && !empty) {
            var tr = document.createElement('tr');
            tr.className = 'pt-empty-row';
            var td = document.createElement('td');
            td.colSpan = table.tHead.rows[0].cells.length;
            td.textContent = table.getAttribute('data-empty') || 'No rows match the current filters';
            tr.appendChild(td);
            tbody.appendChild(tr);
        } else if (any && empty) {
            empty.remove();
        }
    }

    function sort(table, col, dir) {
        var rows = dataRows(table);
        var collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
        rows.sort(function (a, b) {
            var r = collator.compare(text(a.cells[col]), text(b.cells[col]));
            return dir === 'asc' ? r : -r;
        });
        var tbody = table.tBodies[0];
        rows.forEach(function (r) { tbody.appendChild(r); });
    }

    function closeDropdowns() {
        document.querySelectorAll('.pt-cf-dropdown').forEach(function (d) { d.remove(); });
        document.querySelectorAll('.pt-cf.open').forEach(function (b) { b.classList.remove('open'); });
    }

    function openDropdown(table, th, col, btn) {
        var wasOpen = btn.classList.contains('open');
        closeDropdowns();
        if (wasOpen) return;
        btn.classList.add('open');

        var values = Array.from(new Set(dataRows(table).map(function (r) { return text(r.cells[col]); })))
            .sort(function (a, b) { return a.localeCompare(b, undefined, { sensitivity: 'base' }); });
        var current = table.__ptFilters[col];

        var dd = document.createElement('div');
        dd.className = 'pt-cf-dropdown';
        var search = document.createElement('input');
        search.type = 'search';
        search.placeholder = table.getAttribute('data-search') || 'Search...';
        dd.appendChild(search);

        var allLabel = document.createElement('label');
        allLabel.className = 'pt-cf-all';
        var all = document.createElement('input');
        all.type = 'checkbox';
        allLabel.appendChild(all);
        allLabel.appendChild(document.createTextNode(' ' + (table.getAttribute('data-select-all') || '(Select all)')));
        dd.appendChild(allLabel);

        var list = document.createElement('div');
        list.className = 'pt-cf-list';
        values.forEach(function (v) {
            var l = document.createElement('label');
            l.className = 'pt-cf-item';
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.value = v;
            cb.checked = !current || current.has(v);
            var s = document.createElement('span');
            s.textContent = v === '' ? '(—)' : v;
            l.appendChild(cb);
            l.appendChild(s);
            list.appendChild(l);
        });
        dd.appendChild(list);

        function visibleBoxes() {
            return Array.from(list.querySelectorAll('.pt-cf-item')).filter(function (i) { return i.style.display !== 'none'; })
                .map(function (i) { return i.querySelector('input'); });
        }
        function sync() {
            var boxes = visibleBoxes();
            var n = boxes.filter(function (b) { return b.checked; }).length;
            all.checked = boxes.length > 0 && n === boxes.length;
            all.indeterminate = n > 0 && n < boxes.length;
        }
        sync();
        all.addEventListener('change', function () { visibleBoxes().forEach(function (b) { b.checked = all.checked; }); });
        list.addEventListener('change', sync);
        search.addEventListener('input', function () {
            var term = search.value.trim().toLowerCase();
            list.querySelectorAll('.pt-cf-item').forEach(function (i) {
                i.style.display = i.textContent.toLowerCase().indexOf(term) >= 0 ? '' : 'none';
            });
            sync();
        });

        var actions = document.createElement('div');
        actions.className = 'pt-cf-actions';
        var apply = document.createElement('button');
        apply.type = 'button';
        apply.className = 'snap-btn snap-btn-primary';
        apply.textContent = table.getAttribute('data-apply') || 'Apply';
        var clear = document.createElement('button');
        clear.type = 'button';
        clear.className = 'snap-btn';
        clear.textContent = table.getAttribute('data-clear') || 'Clear';
        actions.appendChild(apply);
        actions.appendChild(clear);
        dd.appendChild(actions);

        apply.addEventListener('click', function () {
            var checked = Array.from(list.querySelectorAll('input:checked')).map(function (b) { return b.value; });
            if (checked.length === values.length) { delete table.__ptFilters[col]; th.classList.remove('filtered'); }
            else { table.__ptFilters[col] = new Set(checked); th.classList.add('filtered'); }
            applyFilters(table);
            closeDropdowns();
        });
        clear.addEventListener('click', function () {
            delete table.__ptFilters[col];
            th.classList.remove('filtered');
            applyFilters(table);
            closeDropdowns();
        });
        dd.addEventListener('click', function (e) { e.stopPropagation(); });
        th.appendChild(dd);
        search.focus();
    }

    document.querySelectorAll('table.pt-table').forEach(function (table) {
        table.__ptFilters = {};
        var headers = Array.from(table.tHead.rows[0].cells);
        headers.forEach(function (th, col) {
            th.classList.add('pt-sortable');
            th.tabIndex = 0;
            th.setAttribute('aria-sort', 'none');
            function doSort() {
                var dir = th.getAttribute('aria-sort') === 'ascending' ? 'desc' : 'asc';
                headers.forEach(function (h) { h.setAttribute('aria-sort', 'none'); });
                th.setAttribute('aria-sort', dir === 'asc' ? 'ascending' : 'descending');
                sort(table, col, dir);
            }
            th.addEventListener('click', function (e) { if (!e.target.closest('.pt-cf, .pt-cf-dropdown')) doSort(); });
            th.addEventListener('keydown', function (e) {
                if ((e.key === 'Enter' || e.key === ' ') && e.target === th) { e.preventDefault(); doSort(); }
            });

            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'pt-cf';
            btn.title = table.getAttribute('data-filter') || 'Filter column';
            btn.setAttribute('aria-label', btn.title);
            btn.innerHTML = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></svg>';
            btn.addEventListener('click', function (e) { e.stopPropagation(); openDropdown(table, th, col, btn); });
            th.appendChild(btn);
        });
    });

    document.addEventListener('click', function (e) {
        if (!e.target.closest('.pt-cf-dropdown') && !e.target.closest('.pt-cf')) closeDropdowns();
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeDropdowns(); });
})();
