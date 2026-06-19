// Tek kaynak navigation. Hem desktop (vertical) hem mobile (horizontal) buradan okur.
//
// roles: hangi user_type'lar görebilir (tanımsızsa herkes görür)
// 1=super_admin, 2=team_agent, 3=merchant, 4=sub_admin, 5=team_admin
//
// Parent item'da roles yoksa ve children varsa: parent görünürlüğü children'ın görünürlüğüne bağlıdır.
// filterNav recursive olarak boş children'ı olan parent'ları gizler.

const allNav = [
  {
    title: 'nav.dashboard',
    to: { name: 'dashboard' },
    icon: { icon: 'tabler-layout-dashboard' },
  },
  {
    title: 'nav.deposits',
    icon: { icon: 'tabler-arrow-bar-to-down' },
    children: [
      { title: 'deposits.pending', to: { name: 'deposits-pending' }, icon: { icon: 'tabler-clock' }, roles: [1, 2, 4, 5] },
      { title: 'deposits.all', to: { name: 'deposits-all' }, icon: { icon: 'tabler-list' } },
      { title: 'deposits.converted', to: { name: 'deposits-converted' }, icon: { icon: 'tabler-switch-horizontal' }, roles: [1, 2, 4, 5] },
    ],
  },
  {
    title: 'nav.withdrawals',
    icon: { icon: 'tabler-arrow-bar-up' },
    children: [
      { title: 'withdrawals.pending', to: { name: 'withdrawals-pending' }, icon: { icon: 'tabler-clock' }, roles: [1, 2, 4, 5] },
      { title: 'withdrawals.all', to: { name: 'withdrawals-all' }, icon: { icon: 'tabler-list' } },
      { title: 'withdrawals.receipt_review', to: { name: 'withdrawals-receipt-review' }, icon: { icon: 'tabler-shield-search' }, roles: [1, 4] },
    ],
  },
  {
    title: 'nav.banks',
    to: { name: 'banks' },
    icon: { icon: 'tabler-building-bank' },
    roles: [1, 4, 5],
  },
  {
    title: 'nav.merchants',
    to: { name: 'merchants' },
    icon: { icon: 'tabler-building-store' },
    roles: [1, 4],
  },
  {
    title: 'nav.teams',
    to: { name: 'teams' },
    icon: { icon: 'tabler-users-group' },
    roles: [1, 4],
  },
  {
    title: 'nav.reports',
    icon: { icon: 'tabler-chart-bar' },
    roles: [1, 4, 5],
    children: [
      { title: 'nav.reports', to: { name: 'reports' }, icon: { icon: 'tabler-chart-bar' } },
      { title: 'nav.merchant_reports', to: { name: 'merchant-reports' }, icon: { icon: 'tabler-building-store' }, roles: [1] },
      { title: 'nav.team_reports', to: { name: 'team-reports' }, icon: { icon: 'tabler-users-group' }, roles: [1] },
      { title: 'nav.conversion_reports', to: { name: 'conversion-reports' }, icon: { icon: 'tabler-arrows-exchange' }, roles: [1] },
      { title: 'nav.intermediaries', to: { name: 'intermediaries' }, icon: { icon: 'tabler-users-minus' }, roles: [1] },
      { title: 'nav.fund_storages', to: { name: 'fund-storages' }, icon: { icon: 'tabler-safe' }, roles: [1] },
      { title: 'nav.player_risk', to: { name: 'player-risk' }, icon: { icon: 'tabler-shield-search' }, roles: [1] },
      { title: 'nav.operations', to: { name: 'operations' }, icon: { icon: 'tabler-activity' }, roles: [1] },
      { title: 'nav.blacklist', to: { name: 'blacklist' }, icon: { icon: 'tabler-ban' }, roles: [1, 4] },
    ],
  },
  {
    title: 'nav.cash_report',
    icon: { icon: 'tabler-report-money' },
    roles: [1, 4],
    children: [
      { title: 'nav.site_reports', to: { name: 'merchant-cases' }, icon: { icon: 'tabler-building-store' } },
      { title: 'nav.group_reports', to: { name: 'team-cases' }, icon: { icon: 'tabler-users-group' } },
      { title: 'nav.intermediary_reports', to: { name: 'intermediary-cases' }, icon: { icon: 'tabler-users-minus' } },
      { title: 'nav.general_report', to: { name: 'case-report' }, icon: { icon: 'tabler-report-money' } },
    ],
  },
  {
    title: 'nav.system',
    icon: { icon: 'tabler-settings' },
    roles: [1, 4, 5],
    children: [
      { title: 'nav.users', to: { name: 'users' }, icon: { icon: 'tabler-user-cog' }, roles: [1, 5] },
      { title: 'nav.settings', to: { name: 'settings' }, icon: { icon: 'tabler-settings' }, roles: [1] },
      { title: 'nav.api_logs', to: { name: 'system-logs' }, icon: { icon: 'tabler-list-details' }, roles: [1] },
    ],
  },
]

function filterNav(items, userType) {
  return items
    .filter(item => !item.roles || item.roles.includes(userType))
    .map(item => {
      if (item.children) {
        const filtered = filterNav(item.children, userType)
        if (filtered.length === 0) return null
        return { ...item, children: filtered }
      }
      return item
    })
    .filter(Boolean)
}

function getCurrentUser() {
  try {
    return JSON.parse(localStorage.getItem('user') || '{}')
  } catch {
    return {}
  }
}

const currentUser = getCurrentUser()

// Merchant kullanıcısı için: kendi kasası (firm_id veya merchant_group_id'ye göre)
if (currentUser.user_type === 3 && currentUser.firm_id) {
  const isGroup = !!currentUser.merchant_group_id
  allNav.push({
    title: 'nav.my_case',
    to: {
      name: 'merchant-case-id',
      params: { id: isGroup ? currentUser.merchant_group_id : currentUser.firm_id },
      query: isGroup ? { type: 'group' } : {},
    },
    icon: { icon: 'tabler-cash' },
    roles: [3],
  })
}

export default filterNav(allNav, currentUser.user_type)
