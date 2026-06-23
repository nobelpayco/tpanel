import { setupLayouts } from 'virtual:meta-layouts'
import { createRouter, createWebHistory } from 'vue-router/auto'
import { getBrand } from '@/composables/useBrand'

function recursiveLayouts(route) {
  if (route.children) {
    for (let i = 0; i < route.children.length; i++)
      route.children[i] = recursiveLayouts(route.children[i])
    
    return route
  }
  
  return setupLayouts([route])[0]
}

const router = createRouter({
  history: createWebHistory('/'),
  scrollBehavior(to) {
    if (to.hash)
      return { el: to.hash, behavior: 'smooth', top: 60 }
    
    return { top: 0 }
  },
  extendRoutes: pages => [
    ...[...pages].map(route => recursiveLayouts(route)),
  ],
})

const pageTitles = {
  'dashboard': 'Dashboard',
  'login': 'Giriş',
  'two-factor': '2FA Doğrulama',
  'deposits-pending': 'Bekleyen Yatırımlar',
  'deposits-all': 'Tüm Yatırımlar',
  'deposits-converted': 'Dönüşen Yatırımlar',
  'withdrawals-pending': 'Bekleyen Çekimler',
  'withdrawals-all': 'Tüm Çekimler',
  'banks': 'Banka Hesapları',
  'merchants': 'Merchantlar',
  'teams': 'Takımlar',
  'reports': 'Raporlar',
  'case-report': 'Kasa Raporu',
  'intermediaries': 'Aracılar',
  'team-cases': 'Takım Alacakları',
  'team-case-id': 'Takım Detay',
  'intermediary-cases': 'Aracı Komisyonları',
  'intermediary-case-id': 'Aracı Detay',
  'fund-storages': 'Fon Depoları',
  'merchant-cases': 'Merchant Kasaları',
  'partner-net': 'Partner Net',
  'partner-id': 'Ortak Detay',
  'merchant-case-id': 'Merchant Kasa',
  'blacklist': 'Kara Liste',
  'users': 'Kullanıcılar',
  'settings': 'Ayarlar',
  'wallets': 'Cüzdanlar',
  'wallet-transactions': 'Cüzdan İşlemleri',
}

router.beforeEach((to, from, next) => {
  // Sayfa başlığı
  const title = pageTitles[to.name] || ''
  const brand = getBrand()
  document.title = title ? `${title} - ${brand}` : brand

  const token = localStorage.getItem('token')
  const isPublicPage = to.meta?.public

  // Giriş yapılmışsa login/two-factor sayfalarına gitmesin
  if (token && (to.name === 'login' || to.name === 'two-factor')) {
    return next('/dashboard')
  }

  // Giriş yapılmamışsa public olmayan sayfalara gitmesin
  if (!token && !isPublicPage) {
    return next('/login')
  }

  // Rol kontrolü
  const roles = to.meta?.roles
  if (roles) {
    const user = JSON.parse(localStorage.getItem('user') || '{}')
    if (!roles.includes(user.user_type)) {
      return next('/dashboard')
    }
  }
  // Sistem Yöneticisi (is_sys_admin) kontrolü — Ayarlar + API/Callback Logları
  if (to.meta?.sysAdmin) {
    const user = JSON.parse(localStorage.getItem('user') || '{}')
    if (!user.is_sys_admin) {
      return next('/dashboard')
    }
  }
  next()
})

export { router }
export default function (app) {
  app.use(router)
}
