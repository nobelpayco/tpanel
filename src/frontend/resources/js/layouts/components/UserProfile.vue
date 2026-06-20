<script setup>
import avatar1 from '@images/avatars/avatar-1.png'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useSnackbar } from '@/composables/useSnackbar'
import { useConfigStore } from '@core/stores/config'

const router = useRouter()
const snackbar = useSnackbar()
const configStore = useConfigStore()

const isVerticalNav = computed(() => configStore.appContentLayoutNav === 'vertical')
const toggleLayoutNav = () => {
  const next = isVerticalNav.value ? 'horizontal' : 'vertical'
  configStore.appContentLayoutNav = next
  localStorage.setItem('appContentLayoutNav', next)
}

const user = computed(() => {
  try {
    return JSON.parse(localStorage.getItem('user') || '{}')
  } catch {
    return {}
  }
})

const authHeaders = () => ({
  'Authorization': `Bearer ${localStorage.getItem('token')}`,
  'Accept': 'application/json',
  'Content-Type': 'application/json',
})

const logout = async () => {
  const token = localStorage.getItem('token')

  // Backend logout (token'ı sunucuda da iptal et)
  if (token) {
    try {
      await fetch('/api/auth/logout', { method: 'POST', headers: authHeaders() })
    } catch {
      // sunucu hatası olsa bile client'i temizle
    }
  }

  // Tüm auth state'ini temizle
  localStorage.removeItem('token')
  localStorage.removeItem('user')
  localStorage.removeItem('two_factor_token')
  localStorage.removeItem('two_factor_qr')
  localStorage.removeItem('two_factor_secret')
  sessionStorage.removeItem('two_factor_token')
  sessionStorage.removeItem('two_factor_qr')

  // Full reload — Vue router cache'ini bypass et, temiz session ile login'e git
  window.location.href = '/login'
}

// Şifre değiştirme dialog'u
const showPasswordDialog = ref(false)
const pwLoading = ref(false)
const pwForm = ref({ current_password: '', new_password: '', new_password_confirm: '' })
const showCurrent = ref(false)
const showNew = ref(false)

const openPasswordDialog = () => {
  pwForm.value = { current_password: '', new_password: '', new_password_confirm: '' }
  showCurrent.value = false
  showNew.value = false
  showPasswordDialog.value = true
}

const passwordIsValid = (pw) => pw.length >= 6 && /[A-Za-z]/.test(pw) && /\d/.test(pw)

const submitPasswordChange = async () => {
  if (!pwForm.value.current_password) {
    snackbar.error('Mevcut şifrenizi girin.')
    return
  }
  if (!passwordIsValid(pwForm.value.new_password)) {
    snackbar.error('Yeni şifre en az 6 karakter, en az bir harf ve bir rakam içermelidir.')
    return
  }
  if (pwForm.value.new_password !== pwForm.value.new_password_confirm) {
    snackbar.error('Yeni şifre tekrarı eşleşmiyor.')
    return
  }
  if (pwForm.value.new_password === pwForm.value.current_password) {
    snackbar.error('Yeni şifre eski şifre ile aynı olamaz.')
    return
  }

  pwLoading.value = true
  try {
    const res = await fetch('/api/auth/change-password', {
      method: 'POST',
      headers: authHeaders(),
      body: JSON.stringify({
        current_password: pwForm.value.current_password,
        new_password: pwForm.value.new_password,
      }),
    })
    const data = await res.json()
    if (res.ok) {
      snackbar.success(data.message || 'Şifre güncellendi.')
      showPasswordDialog.value = false
      // Tüm tokenler invalidate edildi — kullanıcıyı login'e yönlendir
      if (data.reauth_required) {
        setTimeout(() => {
          localStorage.removeItem('token')
          localStorage.removeItem('user')
          window.location.href = '/login'
        }, 1200)
      }
    } else {
      snackbar.error(data.message || 'Şifre güncellenemedi.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    pwLoading.value = false
  }
}
</script>

<template>
  <VBadge
    dot
    location="bottom right"
    offset-x="3"
    offset-y="3"
    bordered
    color="success"
  >
    <VAvatar
      class="cursor-pointer"
      color="primary"
      variant="tonal"
    >
      <VImg :src="avatar1" />

      <VMenu
        activator="parent"
        width="240"
        location="bottom end"
        offset="14px"
      >
        <VList>
          <VListItem>
            <template #prepend>
              <VListItemAction start>
                <VBadge
                  dot
                  location="bottom right"
                  offset-x="3"
                  offset-y="3"
                  color="success"
                >
                  <VAvatar
                    color="primary"
                    variant="tonal"
                  >
                    <VImg :src="avatar1" />
                  </VAvatar>
                </VBadge>
              </VListItemAction>
            </template>

            <VListItemTitle class="font-weight-semibold">
              {{ user.name || user.username || '—' }}
            </VListItemTitle>
            <VListItemSubtitle>{{ user.role_label || '' }}</VListItemSubtitle>
          </VListItem>

          <VDivider class="my-2" />

          <VListItem @click="openPasswordDialog">
            <template #prepend>
              <VIcon class="me-2" icon="tabler-key" size="22" />
            </template>
            <VListItemTitle>Şifre Değiştir</VListItemTitle>
          </VListItem>

          <VListItem class="d-none d-lg-flex" @click="toggleLayoutNav">
            <template #prepend>
              <VIcon class="me-2" :icon="isVerticalNav ? 'tabler-layout-navbar' : 'tabler-layout-sidebar-left'" size="22" />
            </template>
            <VListItemTitle>Menü {{ isVerticalNav ? 'Üstte' : 'Solda' }}</VListItemTitle>
          </VListItem>

          <VListItem @click="logout">
            <template #prepend>
              <VIcon class="me-2" icon="tabler-logout" size="22" />
            </template>
            <VListItemTitle>Çıkış</VListItemTitle>
          </VListItem>
        </VList>
      </VMenu>
    </VAvatar>
  </VBadge>

  <!-- Şifre Değiştir Dialog -->
  <VDialog v-model="showPasswordDialog" max-width="450" persistent>
    <VCard title="Şifre Değiştir">
      <VCardText>
        <VRow>
          <VCol cols="12">
            <AppTextField
              v-model="pwForm.current_password"
              :type="showCurrent ? 'text' : 'password'"
              label="Mevcut Şifre"
              density="compact"
              :append-inner-icon="showCurrent ? 'tabler-eye-off' : 'tabler-eye'"
              @click:append-inner="showCurrent = !showCurrent"
            />
          </VCol>
          <VCol cols="12">
            <AppTextField
              v-model="pwForm.new_password"
              :type="showNew ? 'text' : 'password'"
              label="Yeni Şifre"
              density="compact"
              :append-inner-icon="showNew ? 'tabler-eye-off' : 'tabler-eye'"
              @click:append-inner="showNew = !showNew"
            />
          </VCol>
          <VCol cols="12">
            <AppTextField
              v-model="pwForm.new_password_confirm"
              :type="showNew ? 'text' : 'password'"
              label="Yeni Şifre (Tekrar)"
              density="compact"
            />
            <div class="text-caption text-medium-emphasis mt-1">
              Şifre en az 6 karakter olmalı, en az bir harf ve bir rakam içermelidir.
            </div>
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" :disabled="pwLoading" @click="showPasswordDialog = false">İptal</VBtn>
        <VBtn color="primary" :loading="pwLoading" @click="submitPasswordChange">Güncelle</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
