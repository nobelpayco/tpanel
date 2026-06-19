import { ref } from 'vue'
import { useApi } from '@/composables/useApi'

export function useTronLookup(form) {
  const { headers } = useApi()
  const txLoading = ref(false)
  let timeoutId = null

  const lookupTx = async (link) => {
    if (!link || link.length < 64) return
    if (!/[a-f0-9]{64}/i.test(link)) return

    txLoading.value = true
    if (timeoutId) clearTimeout(timeoutId)

    // 10 saniye timeout
    timeoutId = setTimeout(() => { txLoading.value = false }, 10000)

    try {
      const res = await fetch('/api/tron-tx-lookup', {
        method: 'POST', headers,
        body: JSON.stringify({ tx_link: link }),
      })
      if (res.ok) {
        const data = await res.json()
        if (data.quantity) {
          form.value.crypto_quantity = data.quantity
        }
      }
    } catch {} finally {
      txLoading.value = false
      if (timeoutId) { clearTimeout(timeoutId); timeoutId = null }
    }
  }

  return { txLoading, lookupTx }
}
