<script setup>
import { useGenerateImageVariant } from '@core/composable/useGenerateImageVariant'
import authV2LoginIllustrationBorderedDark from '@images/pages/auth-v2-login-illustration-bordered-dark.png'
import authV2LoginIllustrationBorderedLight from '@images/pages/auth-v2-login-illustration-bordered-light.png'
import authV2LoginIllustrationDark from '@images/pages/auth-v2-login-illustration-dark.png'
import authV2LoginIllustrationLight from '@images/pages/auth-v2-login-illustration-light.png'
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

const form = ref({
  username: '',
  password: '',
  remember: false,
})

const isPasswordVisible = ref(false)
const isLoading = ref(false)
const errorMessage = ref('')
const infoMessage = ref('')

// URL'de ?expired=1 varsa: oturum süresi doldu bilgisi göster
if (new URLSearchParams(window.location.search).get('expired') === '1') {
  infoMessage.value = t('auth.session_expired')
}

const authThemeImg = useGenerateImageVariant(
  authV2LoginIllustrationLight,
  authV2LoginIllustrationDark,
  authV2LoginIllustrationBorderedLight,
  authV2LoginIllustrationBorderedDark,
  true,
)
const authThemeMask = useGenerateImageVariant(authV2MaskLight, authV2MaskDark)

const login = async () => {
  isLoading.value = true
  errorMessage.value = ''

  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        'Accept-Language': locale.value,
      },
      body: JSON.stringify({
        username: form.value.username,
        password: form.value.password,
      }),
    })

    const data = await response.json()

    if (!response.ok) {
      errorMessage.value = data.message || t('auth.invalid_credentials')
      return
    }

    if (data.two_factor) {
      // sessionStorage: tab kapanınca otomatik temizlenir — localStorage'da kalıcı saklamayız.
      sessionStorage.setItem('two_factor_token', data.temp_token)
      if (data.setup_required && data.qr_code) {
        sessionStorage.setItem('two_factor_qr', data.qr_code)
      } else {
        sessionStorage.removeItem('two_factor_qr')
      }
      // Olası eski localStorage kalıntılarını temizle
      localStorage.removeItem('two_factor_token')
      localStorage.removeItem('two_factor_qr')
      localStorage.removeItem('two_factor_secret')
      router.push('/two-factor')
      return
    }

    localStorage.setItem('token', data.token)
    localStorage.setItem('user', JSON.stringify(data.user))
    // Full reload — Vue router race condition'ı bypass et, dashboard temiz açılsın
    window.location.href = '/dashboard'
  } catch {
    errorMessage.value = t('auth.invalid_credentials')
  } finally {
    isLoading.value = false
  }
}
</script>

<template>
  <!-- Dil seçici - sağ üst köşe -->
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
          <VImg
            max-width="613"
            :src="authThemeImg"
            class="auth-illustration mt-16 mb-2"
          />
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
            {{ t('auth.welcome_back') }}
          </h4>
          <p class="mb-0">
            {{ t('auth.sign_in_desc') }}
          </p>
        </VCardText>

        <VCardText>
          <VForm @submit.prevent="login">
            <VRow>
              <!-- Bilgi mesajı (oturum süresi dolmuş gibi) -->
              <VCol
                v-if="infoMessage && !errorMessage"
                cols="12"
              >
                <VAlert
                  type="warning"
                  variant="tonal"
                  density="compact"
                >
                  {{ infoMessage }}
                </VAlert>
              </VCol>

              <!-- Hata mesajı -->
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

              <!-- Kullanıcı adı -->
              <VCol cols="12">
                <AppTextField
                  v-model="form.username"
                  autofocus
                  :label="t('auth.username')"
                  :placeholder="t('auth.username')"
                />
              </VCol>

              <!-- Şifre -->
              <VCol cols="12">
                <AppTextField
                  v-model="form.password"
                  :label="t('auth.password')"
                  placeholder="············"
                  :type="isPasswordVisible ? 'text' : 'password'"
                  autocomplete="current-password"
                  :append-inner-icon="isPasswordVisible ? 'tabler-eye-off' : 'tabler-eye'"
                  @click:append-inner="isPasswordVisible = !isPasswordVisible"
                />

                <div class="d-flex align-center flex-wrap justify-space-between my-6">
                  <VCheckbox
                    v-model="form.remember"
                    :label="t('auth.remember_me')"
                  />
                </div>

                <VBtn
                  block
                  type="submit"
                  :loading="isLoading"
                >
                  {{ t('auth.login') }}
                </VBtn>
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
