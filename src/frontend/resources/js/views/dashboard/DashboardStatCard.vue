<script setup>
import { useDashboardStats } from '@/composables/useDashboardStats'

const props = defineProps({
  metric: { type: String, required: true },
})

const { data, loading } = useDashboardStats()

const money = (val) => '₺' + Number(val || 0).toLocaleString('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 0 })
const num = (val) => Number(val || 0).toLocaleString('tr-TR')

const METRICS = {
  deposits: (d) => ({ title: 'Toplam Yatırım', stat: money(d.total_deposits), icon: 'tabler-arrow-bar-to-down', color: 'success', subtitle: 'Seçili dönem' }),
  withdrawals: (d) => ({ title: 'Toplam Çekim', stat: money(d.total_withdrawals), icon: 'tabler-arrow-bar-up', color: 'error', subtitle: 'Seçili dönem' }),
  pending_deposits: (d) => ({ title: 'Bekleyen Yatırım', stat: num(d.pending_deposits), icon: 'tabler-clock', color: 'warning', subtitle: 'Toplam' }),
  pending_withdrawals: (d) => ({ title: 'Bekleyen Çekim', stat: num(d.pending_withdrawals), icon: 'tabler-clock-up', color: 'info', subtitle: money(d.pending_withdrawals_amount) }),
  available_ibans: (d) => ({
    title: 'Kullanılabilir IBAN', stat: num(d.available_ibans_count), icon: 'tabler-credit-card', color: 'primary',
    subtitle: d.available_ibans_count > 0 ? `Min ${money(d.available_ibans_min)} · Max ${money(d.available_ibans_max)}` : '—',
  }),
}

const item = computed(() => (METRICS[props.metric] || METRICS.deposits)(data.value))
</script>

<template>
  <VCard :loading="loading" height="100%" class="d-flex align-center">
    <VCardText class="d-flex justify-space-between align-center w-100">
      <div class="text-truncate">
        <p class="text-body-1 mb-1 text-truncate">{{ item.title }}</p>
        <h4 class="text-h4 font-weight-bold mb-2 text-truncate">{{ item.stat }}</h4>
        <span class="text-body-2 text-medium-emphasis text-truncate d-block">{{ item.subtitle }}</span>
      </div>
      <VAvatar :color="item.color" variant="tonal" rounded size="44" class="flex-shrink-0">
        <VIcon :icon="item.icon" size="28" />
      </VAvatar>
    </VCardText>
  </VCard>
</template>
