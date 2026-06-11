const { createApp } = Vue;

const SHARED_USER = 'devteam';
const SHARED_PASS = 'aircoverage';
const STORE_KEY = 'ac_items_v1';
const AUTH_KEY = 'ac_auth_v1';

const PRIORITIES = ['Critical', 'High', 'Medium', 'Low'];
const STATUSES = ['New', 'In Progress', 'Waiting', 'Resolved', 'Closed'];
const DEVS = ['Alex Reyes', 'Priya Shah', 'Marcus Tran', 'Dana Kim', 'Sam Whitfield'];
const PRIO_RANK = { Critical: 0, High: 1, Medium: 2, Low: 3 };
const STATUS_COLORS = {
  'New': '#3b73c4',
  'In Progress': '#1f9d58',
  'Waiting': '#d8a72b',
  'Resolved': '#2e8b57',
  'Closed': '#9aa0a7',
};

// days-ago ISO helper for seeding
function daysAgo(n) {
  const d = new Date();
  d.setHours(9, 0, 0, 0);
  d.setDate(d.getDate() - n);
  return d.toISOString();
}

function seed() {
  return [
    { id: 'AC-1042', title: 'Production API returning 500s on document upload', description: 'Customers cannot attach files to requests. ~30% of uploads failing since this morning. Storage SDK timeout suspected.', priority: 'Critical', status: 'In Progress', requestedBy: 'On-call', assignee: 'Alex Reyes', ticketType: 'ADO', ticketRef: '#48211', received: daysAgo(0), updated: daysAgo(0) },
    { id: 'AC-1041', title: 'Customer cannot log in after SSO config change', description: 'Acme Co reports redirect loop on login following yesterday\'s identity provider update. Blocking ~40 users.', priority: 'Critical', status: 'New', requestedBy: 'Support', assignee: '', ticketType: 'ConnectWise', ticketRef: '#88204', received: daysAgo(1), updated: null },
    { id: 'AC-1038', title: 'Memory leak in background worker', description: 'Worker process RSS climbs steadily and OOMs about every 6 hours; restarted manually for now.', priority: 'High', status: 'In Progress', requestedBy: 'On-call', assignee: 'Marcus Tran', ticketType: 'ADO', ticketRef: '#48190', received: daysAgo(2), updated: daysAgo(1) },
    { id: 'AC-1035', title: 'Nightly export job failing intermittently', description: 'Scheduled CSV export to the county SFTP fails ~1 in 3 nights with a connection reset. Need retry + alerting.', priority: 'High', status: 'Waiting', requestedBy: 'Support', assignee: 'Priya Shah', ticketType: 'ADO', ticketRef: '#48155', received: daysAgo(4), updated: daysAgo(2) },
    { id: 'AC-1030', title: 'Slow query on Requests dashboard', description: 'Dashboard taking 8–12s to load for tenants with >5k requests. Missing index on status + date likely.', priority: 'High', status: 'New', requestedBy: 'Customer', assignee: '', ticketType: '', ticketRef: '', received: daysAgo(5), updated: null },
    { id: 'AC-1027', title: 'Investigate duplicate notification emails', description: 'A handful of users got the same assignment email 2–3 times. Possibly a webhook re-delivery without idempotency.', priority: 'Medium', status: 'Waiting', requestedBy: 'Support', assignee: 'Dana Kim', ticketType: 'ConnectWise', ticketRef: '#88150', received: daysAgo(8), updated: daysAgo(3) },
    { id: 'AC-1024', title: 'Add retry logic to outbound email service', description: 'Transient SMTP failures currently drop the message silently. Add bounded retry + dead-letter log.', priority: 'Medium', status: 'New', requestedBy: 'Eng', assignee: '', ticketType: 'ADO', ticketRef: '#48099', received: daysAgo(11), updated: null },
    { id: 'AC-1019', title: 'Renew expired SSL cert on staging', description: 'staging.justfoia cert expires next week; renew and automate going forward.', priority: 'Medium', status: 'New', requestedBy: 'Eng', assignee: 'Sam Whitfield', ticketType: '', ticketRef: '', received: daysAgo(15), updated: daysAgo(6) },
    { id: 'AC-1011', title: 'Typo in request confirmation email template', description: '"Recieved" should be "Received" in the auto-confirmation. Minor but customer-facing.', priority: 'Low', status: 'New', requestedBy: 'Support', assignee: '', ticketType: '', ticketRef: '', received: daysAgo(19), updated: null },
    { id: 'AC-1004', title: 'Refresh token rotation edge case', description: 'Token refresh occasionally 401s when two tabs refresh simultaneously. Fix applied, verifying in prod.', priority: 'Critical', status: 'Resolved', requestedBy: 'On-call', assignee: 'Alex Reyes', ticketType: 'ADO', ticketRef: '#47980', received: daysAgo(22), updated: daysAgo(1) },
    { id: 'AC-0998', title: 'Deprecate legacy /v1 report endpoint', description: 'No traffic in 90 days. Removed and redirected; closing out.', priority: 'Low', status: 'Closed', requestedBy: 'Eng', assignee: 'Marcus Tran', ticketType: 'ADO', ticketRef: '#47901', received: daysAgo(30), updated: daysAgo(7) },
  ];
}

function loadItems() {
  try {
    const raw = localStorage.getItem(STORE_KEY);
    if (raw) return JSON.parse(raw);
  } catch (e) { /* ignore */ }
  const s = seed();
  localStorage.setItem(STORE_KEY, JSON.stringify(s));
  return s;
}

createApp({
  data() {
    return {
      SHARED_USER, SHARED_PASS,
      authed: localStorage.getItem(AUTH_KEY) === '1',
      loginForm: { user: '', pass: '' },
      loginError: '',
      avatarOpen: false,

      items: loadItems(),
      priorities: PRIORITIES,
      statuses: STATUSES,
      devs: DEVS,

      search: '',
      activeTab: 'open',
      sortKey: 'priority',
      sortDir: 1, // 1 = critical-first / oldest-first

      editing: null,
      mode: 'edit',

      tabs: [
        { key: 'open', label: 'Open' },
        { key: 'New', label: 'New' },
        { key: 'In Progress', label: 'In Progress' },
        { key: 'Waiting', label: 'Waiting' },
        { key: 'Resolved', label: 'Resolved' },
        { key: 'Closed', label: 'Closed' },
        { key: 'all', label: 'All' },
      ],
    };
  },

  computed: {
    filteredByTab() {
      return this.items.filter(it => {
        if (this.activeTab === 'all') return true;
        if (this.activeTab === 'open') return it.status !== 'Closed' && it.status !== 'Resolved';
        return it.status === this.activeTab;
      });
    },
    filteredBySearch() {
      const q = this.search.trim().toLowerCase();
      if (!q) return this.filteredByTab;
      return this.filteredByTab.filter(it =>
        (it.title || '').toLowerCase().includes(q) ||
        (it.id || '').toLowerCase().includes(q) ||
        (it.requestedBy || '').toLowerCase().includes(q) ||
        (it.assignee || '').toLowerCase().includes(q) ||
        (it.description || '').toLowerCase().includes(q)
      );
    },
    visibleItems() {
      const arr = this.filteredBySearch.slice();
      arr.sort((a, b) => {
        let cmp;
        if (this.sortKey === 'priority') {
          cmp = PRIO_RANK[a.priority] - PRIO_RANK[b.priority];
          if (cmp === 0) cmp = new Date(a.received) - new Date(b.received); // older first
        } else { // received => days open
          cmp = new Date(a.received) - new Date(b.received);
        }
        return cmp * this.sortDir;
      });
      return arr;
    },
    openCount() {
      return this.items.filter(it => it.status !== 'Closed' && it.status !== 'Resolved').length;
    },
    criticalOpen() {
      return this.items.filter(it => it.priority === 'Critical' && it.status !== 'Closed' && it.status !== 'Resolved').length;
    },
  },

  watch: {
    items: { deep: true, handler(v) { localStorage.setItem(STORE_KEY, JSON.stringify(v)); } },
  },

  mounted() {
    document.addEventListener('click', () => { this.avatarOpen = false; });
    window.addEventListener('keydown', (e) => { if (e.key === 'Escape') this.closeModal(); });
  },

  methods: {
    login() {
      if (this.loginForm.user === SHARED_USER && this.loginForm.pass === SHARED_PASS) {
        this.authed = true;
        localStorage.setItem(AUTH_KEY, '1');
        this.loginError = '';
      } else {
        this.loginError = 'Incorrect username or password.';
      }
    },
    signOut() {
      this.authed = false;
      this.avatarOpen = false;
      localStorage.removeItem(AUTH_KEY);
      this.loginForm = { user: '', pass: '' };
    },

    tabCount(key) {
      if (key === 'all') return this.items.length;
      if (key === 'open') return this.openCount;
      return this.items.filter(it => it.status === key).length;
    },
    setSort(key) {
      if (this.sortKey === key) this.sortDir *= -1;
      else { this.sortKey = key; this.sortDir = 1; }
    },

    statusClass(s) { return s.replace(/\s+/g, ''); },
    statusColor(s) { return STATUS_COLORS[s] || '#9aa0a7'; },
    isClosed(s) { return s === 'Closed' || s === 'Resolved'; },
    initials(name) {
      return name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase();
    },
    daysOpen(it) {
      const start = new Date(it.received);
      const end = (it.status === 'Closed') && it.updated ? new Date(it.updated) : new Date();
      return Math.max(0, Math.floor((end - start) / 86400000));
    },
    fmtDate(iso) {
      if (!iso) return '—';
      return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
    },
    fmtDateTime(iso) {
      if (!iso) return '—';
      return new Date(iso).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    },

    nextId() {
      const nums = this.items.map(it => parseInt((it.id || '').replace(/\D/g, ''), 10)).filter(n => !isNaN(n));
      const max = nums.length ? Math.max(...nums) : 1042;
      return 'AC-' + (max + 1);
    },

    openDetail(it) {
      this.mode = 'edit';
      this.editing = JSON.parse(JSON.stringify(it));
    },
    openAdd() {
      this.mode = 'add';
      this.editing = {
        id: this.nextId(), title: '', description: '', priority: 'Medium',
        status: 'New', requestedBy: '', assignee: '', ticketType: '', ticketRef: '',
        received: new Date().toISOString(), updated: null,
      };
    },
    closeModal() { this.editing = null; },

    setStatus(s) {
      if (this.editing) this.editing.status = s;
    },

    save() {
      if (!this.editing.title) return;
      this.editing.updated = new Date().toISOString();
      if (this.mode === 'add') {
        this.items.push(JSON.parse(JSON.stringify(this.editing)));
      } else {
        const idx = this.items.findIndex(i => i.id === this.editing.id);
        if (idx !== -1) this.items.splice(idx, 1, JSON.parse(JSON.stringify(this.editing)));
      }
      this.closeModal();
    },
    removeItem() {
      const idx = this.items.findIndex(i => i.id === this.editing.id);
      if (idx !== -1) this.items.splice(idx, 1);
      this.closeModal();
    },
  },
}).mount('#app');
