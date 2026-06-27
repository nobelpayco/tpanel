<script setup>
import DashboardStatCard from '@/views/dashboard/DashboardStatCard.vue'
import DashboardTransactionChart from '@/views/dashboard/DashboardTransactionChart.vue'
import DashboardWeeklyOverview from '@/views/dashboard/DashboardWeeklyOverview.vue'
import DashboardRecentTransactions from '@/views/dashboard/DashboardRecentTransactions.vue'
import DashboardTeamPerformance from '@/views/dashboard/DashboardTeamPerformance.vue'
import { GridLayout, GridItem } from 'grid-layout-plus'
import { setStatsRange, stopStats } from '@/composables/useDashboardStats'
import { useSnackbar } from '@/composables/useSnackbar'
import { useI18n } from 'vue-i18n'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()
const snackbar = useSnackbar()

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isMerchant = user.user_type === 3

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const dateFrom = ref(today)
const dateTo = ref(today)

const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const refreshKey = ref(0)
const applyFilter = () => { refreshKey.value++; setStatsRange(dateFrom.value, dateTo.value) }

// ---- Widget kataloğu ----
const COMPONENTS = { DashboardStatCard, DashboardTransactionChart, DashboardWeeklyOverview, DashboardRecentTransactions, DashboardTeamPerformance }
// NOT: grid 10 sütunlu (5 stat kartı tek satıra tam otursun diye).
const WIDGETS = {
  stat_deposits:    { title: 'Toplam Yatırım',     comp: 'DashboardStatCard', props: { metric: 'deposits' },            minW: 2, minH: 2, defW: 2, defH: 2 },
  stat_withdrawals: { title: 'Toplam Çekim',        comp: 'DashboardStatCard', props: { metric: 'withdrawals' },         minW: 2, minH: 2, defW: 2, defH: 2 },
  stat_pending_dep: { title: 'Bekleyen Yatırım',    comp: 'DashboardStatCard', props: { metric: 'pending_deposits' },    minW: 2, minH: 2, defW: 2, defH: 2 },
  stat_pending_wd:  { title: 'Bekleyen Çekim',      comp: 'DashboardStatCard', props: { metric: 'pending_withdrawals' }, minW: 2, minH: 2, defW: 2, defH: 2 },
  stat_ibans:       { title: 'Kullanılabilir IBAN', comp: 'DashboardStatCard', props: { metric: 'available_ibans' },     minW: 2, minH: 2, defW: 2, defH: 2 },
  chart:            { title: 'İşlem Hacmi',         comp: 'DashboardTransactionChart',   minW: 3, minH: 4, defW: 6, defH: 7 },
  cases:            { title: 'Anlık Kasalar',       comp: 'DashboardWeeklyOverview',     minW: 3, minH: 4, defW: 4, defH: 7 },
  recent:           { title: 'Son İşlemler',        comp: 'DashboardRecentTransactions', minW: 3, minH: 4, defW: 6, defH: 7 },
  team_perf:        { title: 'Takım Performansı',   comp: 'DashboardTeamPerformance',    minW: 3, minH: 4, defW: 4, defH: 7, hideForMerchant: true },
}

const allowedIds = Object.keys(WIDGETS).filter(id => !(isMerchant && WIDGETS[id].hideForMerchant))

const defaultLayout = () => ([
  { i: 'stat_deposits',    x: 0, y: 0, w: 2, h: 2 },
  { i: 'stat_withdrawals', x: 2, y: 0, w: 2, h: 2 },
  { i: 'stat_pending_dep', x: 4, y: 0, w: 2, h: 2 },
  { i: 'stat_pending_wd',  x: 6, y: 0, w: 2, h: 2 },
  { i: 'stat_ibans',       x: 8, y: 0, w: 2, h: 2 },
  { i: 'chart',            x: 0, y: 2, w: 6, h: 7 },
  { i: 'cases',            x: 6, y: 2, w: 4, h: 7 },
  { i: 'recent',           x: 0, y: 9, w: 6, h: 7 },
  { i: 'team_perf',        x: 6, y: 9, w: 4, h: 7 },
].filter(it => allowedIds.includes(it.i)))

// ---- Durum ----
const editMode = ref(false)
const layout = ref([])
const savedSnapshot = ref('[]')
const saving = ref(false)

const availableToAdd = computed(() => allowedIds.filter(id => !layout.value.some(it => it.i === id)))

const loadLayout = async () => {
  let result = null
  try {
    const token = localStorage.getItem('token')
    const res = await fetch('/api/dashboard/layout', { headers: { Authorization: `Bearer ${token}` } })
    if (res.ok) {
      const d = await res.json()
      if (d.layout) {
        const parsed = JSON.parse(d.layout)
        if (Array.isArray(parsed)) {
          const valid = parsed.filter(it => it && WIDGETS[it.i] && allowedIds.includes(it.i))
          if (valid.length) result = valid.map(it => ({ i: it.i, x: +it.x || 0, y: +it.y || 0, w: +it.w || WIDGETS[it.i].defW, h: +it.h || WIDGETS[it.i].defH }))
        }
      }
    }
  } catch { /* yoksa varsayılan */ }
  layout.value = result || defaultLayout()
  savedSnapshot.value = JSON.stringify(layout.value)
}

const addWidget = (id) => {
  if (layout.value.some(it => it.i === id)) return
  const maxY = layout.value.reduce((m, it) => Math.max(m, it.y + it.h), 0)
  layout.value.push({ i: id, x: 0, y: maxY, w: WIDGETS[id].defW, h: WIDGETS[id].defH })
}
const removeWidget = (id) => { layout.value = layout.value.filter(it => it.i !== id) }

const startEdit = () => { savedSnapshot.value = JSON.stringify(layout.value); editMode.value = true }
const cancelEdit = () => { layout.value = JSON.parse(savedSnapshot.value); editMode.value = false }
const resetDefault = () => { layout.value = defaultLayout() }

const saveLayout = async () => {
  saving.value = true
  try {
    const token = localStorage.getItem('token')
    const payload = layout.value.map(it => ({ i: it.i, x: it.x, y: it.y, w: it.w, h: it.h }))
    const res = await fetch('/api/dashboard/layout', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ layout: JSON.stringify(payload) }),
    })
    if (res.ok) { savedSnapshot.value = JSON.stringify(layout.value); editMode.value = false; snackbar.success('Dashboard düzeni kaydedildi.') }
    else snackbar.error('Kaydedilemedi.')
  } catch { snackbar.error('Sunucu hatası.') } finally { saving.value = false }
}

onMounted(() => { loadLayout(); setStatsRange(dateFrom.value, dateTo.value) })
onUnmounted(() => stopStats())
</script>

<template>
  <div>
    <!-- Üst bar: tarih filtresi + düzenleme -->
    <VCard class="mb-4">
      <VCardText class="d-flex align-center gap-4 flex-wrap">
        <div style="min-width: 160px;">
          <AppDateTimePicker v-model="dateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
        </div>
        <div style="min-width: 160px;">
          <AppDateTimePicker v-model="dateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
        </div>
        <VBtn color="primary" @click="applyFilter">{{ t('common.filter') }}</VBtn>
        <VBtn v-if="dateFrom !== today || dateTo !== today" variant="text" color="secondary" @click="dateFrom = today; dateTo = today; applyFilter()">
          {{ t('dashboard.today') }}
        </VBtn>

        <VSpacer />

        <!-- Düzenleme kontrolleri -->
        <template v-if="!editMode">
          <VBtn variant="tonal" color="secondary" prepend-icon="tabler-layout-dashboard" @click="startEdit">Düzenle</VBtn>
        </template>
        <template v-else>
          <VMenu v-if="availableToAdd.length">
            <template #activator="{ props }">
              <VBtn v-bind="props" variant="tonal" color="primary" prepend-icon="tabler-plus">Kutucuk Ekle</VBtn>
            </template>
            <VList density="compact">
              <VListItem v-for="id in availableToAdd" :key="id" :title="WIDGETS[id].title" @click="addWidget(id)">
                <template #prepend><VIcon icon="tabler-square-plus" size="18" class="me-2" /></template>
              </VListItem>
            </VList>
          </VMenu>
          <VBtn variant="text" color="warning" prepend-icon="tabler-refresh" @click="resetDefault">Varsayılana Dön</VBtn>
          <VBtn variant="text" @click="cancelEdit">İptal</VBtn>
          <VBtn color="success" prepend-icon="tabler-device-floppy" :loading="saving" @click="saveLayout">Kaydet</VBtn>
        </template>
      </VCardText>
    </VCard>

    <div v-if="editMode" class="text-caption text-medium-emphasis mb-2">
      <VIcon icon="tabler-info-circle" size="14" /> Kutucukları taşımak için <strong>taşı simgesinden</strong> sürükleyin, boyutlandırmak için sağ-alt köşeden çekin.
    </div>

    <GridLayout
      v-model:layout="layout"
      :col-num="10"
      :row-height="50"
      :margin="[10, 10]"
      :is-draggable="editMode"
      :is-resizable="editMode"
      :vertical-compact="true"
      :responsive="false"
      :use-css-transforms="true"
    >
      <GridItem
        v-for="item in layout"
        :key="item.i"
        :x="item.x"
        :y="item.y"
        :w="item.w"
        :h="item.h"
        :i="item.i"
        :min-w="WIDGETS[item.i].minW"
        :min-h="WIDGETS[item.i].minH"
        drag-allow-from=".widget-handle"
        class="dash-grid-item"
        :class="{ 'edit-on': editMode }"
      >
        <div class="widget-box">
          <div v-if="editMode" class="widget-toolbar">
            <VIcon icon="tabler-arrows-move" size="16" class="widget-handle" />
            <span class="text-caption font-weight-medium ms-1 text-truncate">{{ WIDGETS[item.i].title }}</span>
            <VSpacer />
            <VBtn icon size="x-small" variant="text" color="error" title="Kaldır" @click="removeWidget(item.i)">
              <VIcon icon="tabler-x" size="16" />
            </VBtn>
          </div>
          <div class="widget-content">
            <component
              :is="COMPONENTS[WIDGETS[item.i].comp]"
              v-bind="WIDGETS[item.i].props || {}"
              :date-from="dateFrom"
              :date-to="dateTo"
              :key="item.i + '-' + refreshKey"
            />
          </div>
        </div>
      </GridItem>
    </GridLayout>
  </div>
</template>

<style scoped>
.widget-box { block-size: 100%; position: relative; }
.widget-toolbar {
  position: absolute; inset-block-start: 0; inset-inline: 0;
  display: flex; align-items: center; gap: 2px;
  block-size: 22px; padding: 0 4px 0 6px;
  background: rgba(var(--v-theme-surface), 0.9);
  border-radius: 8px 8px 0 0;
  z-index: 5;
}
.widget-handle { cursor: move; }
.widget-content { block-size: 100%; overflow: auto; }
.widget-content :deep(.v-card) { block-size: 100%; }
.edit-on {
  outline: 2px dashed rgba(var(--v-theme-primary), 0.45);
  outline-offset: 2px;
  border-radius: 8px;
}
.dash-grid-item :deep(.vgl-item__resizer) { z-index: 6; }
</style>
