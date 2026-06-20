<script setup>
import { useTheme } from 'vuetify'
import { useI18n } from 'vue-i18n'
import { watch } from 'vue'
import ScrollToTop from '@core/components/ScrollToTop.vue'
import initCore from '@core/initCore'
import {
  initConfigStore,
  useConfigStore,
} from '@core/stores/config'
import { hexToRgb } from '@core/utils/colorConverter'
import { themeConfig } from '@themeConfig'
import 'vue3-toastify/dist/index.css'

const { global } = useTheme()

initCore()
initConfigStore()

const configStore = useConfigStore()

// Kullanıcının tercih ettiği menü konumunu (sol/üst) localStorage'dan yükle
const savedLayoutNav = localStorage.getItem('appContentLayoutNav')
if (savedLayoutNav === 'vertical' || savedLayoutNav === 'horizontal') {
  configStore.appContentLayoutNav = savedLayoutNav
}

// Dil değişince RTL'yi langConfig'teki isRTL'e göre senkronize et (UR gibi RTL diller için)
const { locale } = useI18n()
const syncRtlFromLocale = code => {
  const entry = themeConfig.app.i18n.langConfig?.find(l => l.i18nLang === code)
  configStore.isAppRTL = !!entry?.isRTL
}
syncRtlFromLocale(locale.value)
watch(locale, syncRtlFromLocale)
</script>

<template>
  <VLocaleProvider :rtl="configStore.isAppRTL">
    <VApp :style="`--v-global-theme-primary: ${hexToRgb(global.current.value.colors.primary)}`">
      <RouterView />

      <ScrollToTop />
    </VApp>
  </VLocaleProvider>
</template>
