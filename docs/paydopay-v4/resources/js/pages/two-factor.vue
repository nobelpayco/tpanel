<script setup>
import { useGenerateImageVariant } from '@core/composable/useGenerateImageVariant'
import authV2MaskDark from '@images/pages/misc-mask-dark.png'
import authV2MaskLight from '@images/pages/misc-mask-light.png'
import { VNodeRenderer } from '@layouts/components/VNodeRenderer'
import { themeConfig } from '@themeConfig'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import LanguageSwitcher from '@/components/LanguageSwitcher.vue'

definePage({
  meta: {
    layout: 'blank',
    public: true,
  },
})

const { t, locale } = useI18n()
const router = useRouter()

// Token yoksa login'e yönlendir
const storedToken = sessionStorage.getItem('two_factor_token')
if (!storedToken) {
  router.replace('/login')
}

const code = ref('')
const isLoading = ref(false)
const errorMessage = ref('')
const authThemeMask = useGenerateImageVariant(authV2MaskLight, authV2MaskDark)

// Login sayfasından gelen veriler — sessionStorage (tab-bound, kalıcı değil)
const tempToken = ref(storedToken || '')
const qrCodeRaw = sessionStorage.getItem('two_factor_qr') || ''
const qrCodeSvg = ref(qrCodeRaw ? window.atob(qrCodeRaw) : '')
const isSetup = ref(!!qrCodeRaw)

const verify = async () => {
  if (code.value.length !== 6) return

  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await fetch('/api/auth/two-factor', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        'Accept-Language': locale.value,
      },
      body: JSON.stringify({
        temp_token: tempToken.value,
        code: code.value,
      }),
    })

    const data = await response.json()

    if (!response.ok) {
      errorMessage.value = data.message || t('auth.invalid_code')
      code.value = ''
      return
    }

    localStorage.setItem('token', data.token)
    localStorage.setItem('user', JSON.stringify(data.user))
    sessionStorage.removeItem('two_factor_token')
    sessionStorage.removeItem('two_factor_qr')
    router.push('/dashboard')
  } catch {
    errorMessage.value = t('auth.invalid_code')
  } finally {
    isLoading.value = false
  }
}
</script>

<template>
  <!-- Dil seçici -->
  <div style="position: fixed; top: 16px; right: 24px; z-index: 9999;">
    <LanguageSwitcher size="default" />
  </div>

  <div class="auth-logo d-flex align-center gap-x-3">
    <VNodeRenderer :nodes="themeConfig.app.logo" />
    <h1 class="auth-title">
      {{ themeConfig.app.title }}
    </h1>
  </div>

  <VRow
    no-gutters
    class="auth-wrapper bg-surface"
  >
    <VCol
      md="8"
      class="d-none d-md-flex"
    >
      <div class="position-relative bg-background w-100 me-0">
        <div
          class="d-flex align-center justify-center w-100 h-100"
          style="padding-inline: 6.25rem;"
        >
          <div class="text-center">
            <VIcon
              icon="tabler-shield-lock"
              size="120"
              color="primary"
              class="mb-6"
            />
            <h2 class="text-h4">
              {{ t('auth.two_factor') }}
            </h2>
          </div>
        </div>
        <img
          class="auth-footer-mask flip-in-rtl"
          :src="authThemeMask"
          alt="auth-footer-mask"
          height="280"
          width="100"
        >
      </div>
    </VCol>

    <VCol
      cols="12"
      md="4"
      class="auth-card-v2 d-flex align-center justify-center"
    >
      <VCard
        flat
        :max-width="500"
        class="mt-12 mt-sm-0 pa-6"
      >
        <VCardText>
          <h4 class="text-h4 mb-1">
            {{ t('auth.two_factor') }}
          </h4>
          <p class="mb-0">
            {{ isSetup ? t('auth.two_factor_setup_desc') : t('auth.two_factor_desc') }}
          </p>
        </VCardText>

        <VCardText>
          <!-- QR Kod kurulumu — secret string ekranda gösterilmez, sadece QR kullanıcı tarafından taranır -->
          <div
            v-if="isSetup"
            class="text-center mb-6"
          >
            <div
              class="d-inline-block pa-3 rounded border"
              style="background: white;"
              v-html="qrCodeSvg"
            />
          </div>

          <VForm @submit.prevent="verify">
            <VRow>
              <VCol
                v-if="errorMessage"
                cols="12"
              >
                <VAlert
                  type="error"
                  variant="tonal"
                  density="compact"
                >
                  {{ errorMessage }}
                </VAlert>
              </VCol>

              <VCol cols="12">
                <AppTextField
                  v-model="code"
                  autofocus
                  :label="t('auth.verification_code')"
                  placeholder="000000"
                  maxlength="6"
                  class="text-center"
                  style="letter-spacing: 0.5em; font-size: 1.5rem;"
                />
              </VCol>

              <VCol cols="12">
                <VBtn
                  block
                  type="submit"
                  :loading="isLoading"
                  :disabled="code.length !== 6"
                >
                  {{ isSetup ? t('auth.activate_and_login') : t('auth.verify') }}
                </VBtn>
              </VCol>

              <VCol
                cols="12"
                class="text-center"
              >
                <a
                  class="text-primary text-body-2 cursor-pointer"
                  @click="router.push('/login')"
                >
                  {{ t('auth.login') }}
                </a>
              </VCol>
            </VRow>
          </VForm>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>

<style lang="scss">
@use "@core-scss/template/pages/page-auth";
</style>
