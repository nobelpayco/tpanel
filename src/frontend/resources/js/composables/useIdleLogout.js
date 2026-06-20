import { onMounted, onUnmounted } from 'vue'

/**
 * Kullanıcı hareketsiz kaldığında otomatik oturum kapatır.
 *
 * @param {number} timeoutMs  inactivity süresi (varsayılan 30dk)
 */
export function useIdleLogout(timeoutMs = 30 * 60 * 1000) {
  let timer = null

  const events = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'scroll', 'click']

  const reset = () => {
    if (timer) clearTimeout(timer)
    timer = setTimeout(doLogout, timeoutMs)
  }

  const doLogout = async () => {
    const token = localStorage.getItem('token')
    if (token) {
      try {
        await fetch('/api/auth/logout', {
          method: 'POST',
          headers: { 'Authorization': `Bearer ${token}`, 'Accept': 'application/json' },
        })
      } catch {}
    }
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    localStorage.removeItem('two_factor_token')
    localStorage.removeItem('two_factor_qr')
    localStorage.removeItem('two_factor_secret')
    window.location.href = '/login?expired=1'
  }

  onMounted(() => {
    events.forEach(e => window.addEventListener(e, reset, { passive: true }))
    reset()
  })

  onUnmounted(() => {
    if (timer) clearTimeout(timer)
    events.forEach(e => window.removeEventListener(e, reset))
  })
}
