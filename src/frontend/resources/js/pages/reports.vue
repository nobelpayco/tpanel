<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'

definePage({ meta: { layout: 'default', roles: [1, 4, 5] } })

const router = useRouter()
const authUser = JSON.parse(localStorage.getItem('user') || '{}')
const userType = authUser.user_type

const performanceReports = [
  {
    name: 'merchant-reports',
    icon: 'tabler-building-store',
    color: '#4F46E5',
    title: 'Merchant Raporları',
    desc: 'Site bazında hacim, oyuncu, tutar dağılımı ve finansal analizler. Komisyon geliri, net kasa değişimi ve risk göstergeleri.',
    roles: [1],
  },
  {
    name: 'team-reports',
    icon: 'tabler-users-group',
    color: '#059669',
    title: 'Takım Raporları',
    desc: 'Takım bazında genel performans, trendler ve saatlik aktivite. Onay/red oranları, ortalama süreler ve hacim karşılaştırması.',
    roles: [1],
  },
  {
    name: 'conversion-reports',
    icon: 'tabler-arrows-exchange',
    color: '#0891B2',
    title: 'Dönüşüm Raporu',
    desc: 'Yatırım/çekim dönüşüm oranlarının zaman içindeki seyri. Merchant ve takım bazında onay oranı karşılaştırması.',
    roles: [1],
  },
  {
    name: 'player-risk',
    icon: 'tabler-shield-search',
    color: '#DC2626',
    title: 'Oyuncu Risk Analizi',
    desc: 'Şüpheli davranışlar (multi-name, hızlı çekim, yüksek red oranı), oyuncu segmentasyonu ve risk skorları.',
    roles: [1],
  },
  {
    name: 'operations',
    icon: 'tabler-activity',
    color: '#D97706',
    title: 'Operasyonel Raporlar',
    desc: 'Kuyruk analizi, peak saat dağılımı ve SLA metrikleri. İşlem yoğunluğu ve agent performansı göstergeleri.',
    roles: [1],
  },
]

const caseReports = [
  {
    name: 'merchant-cases',
    icon: 'tabler-building-store',
    color: '#7C3AED',
    title: 'Site Raporları',
    desc: 'Her merchant için günlük kasa bakiyesi, yatırım/çekim/ödeme akışı ve grup bazında konsolide kasa görünümü.',
    roles: [1, 4],
  },
  {
    name: 'team-cases',
    icon: 'tabler-users-group',
    color: '#0E7490',
    title: 'Grup Raporları',
    desc: 'Takım bazında alacak/borç (overturn) durumu, günlük transfer ve senkronizasyon kayıtları.',
    roles: [1, 4],
  },
  {
    name: 'intermediary-cases',
    icon: 'tabler-users-minus',
    color: '#B45309',
    title: 'Aracı Raporları',
    desc: 'Aracı komisyon kasaları, merchant/takım bazlı kazanç dağılımı ve ödeme geçmişi.',
    roles: [1, 4],
  },
  {
    name: 'case-report',
    icon: 'tabler-report-money',
    color: '#1F2937',
    title: 'Genel Rapor',
    desc: 'Tüm sistem kasalarının (merchant, takım, aracı, paylira-net, fon deposu) anlık ve dönemsel görünümü.',
    roles: [1, 4],
  },
]

const visiblePerformance = computed(() => performanceReports.filter(r => r.roles.includes(userType)))
const visibleCases = computed(() => caseReports.filter(r => r.roles.includes(userType)))

const go = (name) => router.push({ name })
</script>

<template>
  <div class="reports-index">
    <div class="page-head">
      <h1 class="page-title">Raporlar</h1>
      <p class="page-sub">Sistemdeki tüm raporlar ve ne içerdiklerinin özeti. Detaya gitmek için kart üzerine tıklayın.</p>
    </div>

    <template v-if="visiblePerformance.length">
      <h2 class="section-title">Performans Raporları</h2>
      <div class="report-grid">
        <VCard
          v-for="r in visiblePerformance"
          :key="r.name"
          class="report-card"
          :ripple="false"
          @click="go(r.name)"
        >
          <div class="report-icon" :style="{ background: r.color + '14', color: r.color }">
            <VIcon :icon="r.icon" size="28" />
          </div>
          <div class="report-body">
            <h3 class="report-title">{{ r.title }}</h3>
            <p class="report-desc">{{ r.desc }}</p>
          </div>
          <VIcon icon="tabler-arrow-up-right" class="report-arrow" size="20" />
        </VCard>
      </div>
    </template>

    <template v-if="visibleCases.length">
      <h2 class="section-title">Kasa Raporları</h2>
      <div class="report-grid">
        <VCard
          v-for="r in visibleCases"
          :key="r.name"
          class="report-card"
          :ripple="false"
          @click="go(r.name)"
        >
          <div class="report-icon" :style="{ background: r.color + '14', color: r.color }">
            <VIcon :icon="r.icon" size="28" />
          </div>
          <div class="report-body">
            <h3 class="report-title">{{ r.title }}</h3>
            <p class="report-desc">{{ r.desc }}</p>
          </div>
          <VIcon icon="tabler-arrow-up-right" class="report-arrow" size="20" />
        </VCard>
      </div>
    </template>

    <div v-if="!visiblePerformance.length && !visibleCases.length" class="empty-state">
      <VIcon icon="tabler-lock" size="48" class="mb-2" />
      <p>Görüntüleyebileceğiniz rapor yok.</p>
    </div>
  </div>
</template>

<style scoped>
.reports-index {
  padding: 8px 0 32px;
}

.page-head {
  margin-bottom: 24px;
}
.page-title {
  font-size: 1.8rem;
  font-weight: 700;
  letter-spacing: -0.02em;
  margin-bottom: 6px;
}
.page-sub {
  color: rgba(var(--v-theme-on-surface), 0.65);
  font-size: 0.95rem;
}

.section-title {
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: rgba(var(--v-theme-on-surface), 0.55);
  margin: 32px 0 12px;
}
.section-title:first-of-type {
  margin-top: 8px;
}

.report-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.report-card {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding: 20px;
  cursor: pointer;
  transition: transform 0.15s, box-shadow 0.15s;
  border-radius: 12px !important;
  border: 1px solid rgba(var(--v-theme-on-surface), 0.07);
}
.report-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 24px rgba(15, 23, 42, 0.08) !important;
  border-color: rgba(var(--v-theme-primary), 0.3);
}
.report-card:hover .report-arrow {
  opacity: 1;
  transform: translate(0, 0);
}

.report-icon {
  flex-shrink: 0;
  width: 52px;
  height: 52px;
  display: grid;
  place-items: center;
  border-radius: 10px;
}

.report-body {
  flex: 1;
  min-width: 0;
}
.report-title {
  font-size: 1.05rem;
  font-weight: 600;
  margin-bottom: 4px;
  letter-spacing: -0.01em;
}
.report-desc {
  font-size: 0.85rem;
  line-height: 1.5;
  color: rgba(var(--v-theme-on-surface), 0.7);
  margin: 0;
}

.report-arrow {
  position: absolute;
  top: 16px;
  right: 16px;
  opacity: 0;
  transform: translate(-4px, 4px);
  transition: opacity 0.2s, transform 0.2s;
  color: rgb(var(--v-theme-primary));
}

.empty-state {
  text-align: center;
  padding: 64px 16px;
  color: rgba(var(--v-theme-on-surface), 0.45);
}

@media (max-width: 600px) {
  .report-grid { grid-template-columns: 1fr; }
  .page-title { font-size: 1.5rem; }
}
</style>
