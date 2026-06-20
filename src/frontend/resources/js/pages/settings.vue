<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { getBrand } from '@/composables/useBrand'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()
const brand = getBrand()

const activeTab = ref('notifications')
const loading = ref(false)
const saving = ref(false)

const settings = ref({
  payroute_alert_enabled: 1,
  min_notify_amount: 100,
  telegram_bot_token: '',
  telegram_chat_id: '',
  pay_link_expiry_enabled: 0,
  pay_link_expiry_minutes: 15,
  anthropic_api_key: '',
  anthropic_vision_model: 'claude-haiku-4-5',
})

const anthropicKeySet = ref(false)
const anthropicKeyMasked = ref('')
const anthropicUsage = ref(null)
const anthropicTesting = ref(false)
const anthropicTestResult = ref(null)

// Manuel dekont test
const testForm = ref({ amount: '', iban: '', recipient: '', file_base64: '', file_name: '', mime_type: '' })
const testFileRef = ref(null)
const testAnalyzing = ref(false)
const testResult = ref(null)

const fileToBase64 = (file) => new Promise((resolve, reject) => {
  const r = new FileReader()
  r.onload = () => resolve(String(r.result))
  r.onerror = () => reject(r.error)
  r.readAsDataURL(file)
})

const onTestFileSelect = async (e) => {
  const f = e.target.files && e.target.files[0]
  if (!f) return
  const allowed = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp']
  if (!allowed.includes(f.type)) { snackbar.error('Sadece PDF/JPG/PNG/WEBP'); return }
  if (f.size > 10 * 1024 * 1024) { snackbar.error('Dosya 10MB\'ı aşamaz'); return }
  testForm.value.file_base64 = await fileToBase64(f)
  testForm.value.file_name = f.name
  testForm.value.mime_type = f.type
}

const testAnthropicConnection = async () => {
  anthropicTesting.value = true
  anthropicTestResult.value = null
  try {
    const res = await fetch('/api/settings/anthropic/test', { method: 'POST', headers })
    const data = await res.json()
    anthropicTestResult.value = { ok: res.ok && data.ok, message: data.message || (res.ok ? 'OK' : 'Hata') }
  } catch (e) {
    anthropicTestResult.value = { ok: false, message: 'Sunucu hatası.' }
  } finally {
    anthropicTesting.value = false
  }
}

const analyzeManualReceipt = async () => {
  if (!testForm.value.file_base64) { snackbar.error('Önce bir dosya seç.'); return }
  testAnalyzing.value = true
  testResult.value = null
  try {
    const res = await fetch('/api/settings/anthropic/analyze-test', {
      method: 'POST', headers, body: JSON.stringify(testForm.value),
    })
    const data = await res.json()
    if (res.ok) {
      testResult.value = data
    } else {
      snackbar.error(data.message || 'Analiz başarısız')
    }
  } catch (e) {
    snackbar.error('Sunucu hatası')
  } finally {
    testAnalyzing.value = false
  }
}

// Chat ID bulma helper
const groupName = ref('')
const findingChatId = ref(false)
const chatIdMatches = ref([])
const chatIdHint = ref('')
const showBotToken = ref(false)

const fetchSettings = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/settings', { headers })
    if (res.ok) {
      const data = await res.json()
      Object.keys(settings.value).forEach(k => {
        if (data[k] !== undefined && data[k] !== null) {
          // String alanlar sayıya çevrilmesin
          if (['telegram_bot_token','telegram_chat_id','anthropic_api_key','anthropic_vision_model'].includes(k)) {
            settings.value[k] = String(data[k])
          } else {
            settings.value[k] = isNaN(Number(data[k])) ? data[k] : Number(data[k])
          }
        }
      })
      // Anthropic özel alanlar (masked + usage + key set)
      anthropicKeySet.value = !!data.anthropic_api_key_set
      anthropicKeyMasked.value = data.anthropic_api_key_masked || ''
      anthropicUsage.value = data.anthropic_usage || null
      // Form input'ta key girişi için boş başlat — eski key'i göstermiyoruz
      settings.value.anthropic_api_key = ''
    }
  } finally {
    loading.value = false
  }
}

const findChatId = async () => {
  if (!groupName.value.trim()) { snackbar.error('Grup adı girin.'); return }
  findingChatId.value = true
  chatIdMatches.value = []
  chatIdHint.value = ''
  try {
    const res = await fetch('/api/settings/telegram/find-chat-id', {
      method: 'POST', headers,
      body: JSON.stringify({ group_name: groupName.value.trim() }),
    })
    const data = await res.json()
    if (!res.ok) { snackbar.error(data.message || 'Sorgu başarısız.'); return }
    chatIdMatches.value = data.matches || []
    chatIdHint.value = data.hint || ''
    if (chatIdMatches.value.length === 0 && (data.all_chats || []).length > 0) {
      chatIdHint.value = 'Aramayla eşleşen grup yok. Botun gördüğü gruplar: ' +
        data.all_chats.map(c => `"${c.title}"`).join(', ')
    } else if (chatIdMatches.value.length > 0) {
      snackbar.success(chatIdMatches.value.length + ' grup bulundu.')
    }
  } catch (e) {
    snackbar.error('Sunucu hatası.')
  } finally {
    findingChatId.value = false
  }
}

const useChatId = (id) => {
  settings.value.telegram_chat_id = String(id)
  snackbar.success('Chat ID alana yazıldı. Kaydet butonuna basmayı unutmayın.')
}

const saveSettings = async () => {
  saving.value = true
  try {
    const payload = { ...settings.value }
    // anthropic_api_key boşsa payload'tan çıkar (mevcut key silinmesin)
    if (!payload.anthropic_api_key) delete payload.anthropic_api_key
    const res = await fetch('/api/settings', {
      method: 'PUT', headers,
      body: JSON.stringify({ settings: payload }),
    })
    if (res.ok) { snackbar.success('Ayarlar kaydedildi.'); fetchSettings() }
    else snackbar.handleError(await res.json())
  } finally {
    saving.value = false
  }
}

onMounted(fetchSettings)
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>Sistem Ayarları</VCardTitle>
        </VCardItem>
        <VDivider />

        <VTabs v-model="activeTab">
          <VTab value="notifications">
            <VIcon icon="tabler-bell" size="18" class="me-1" />
            Bildirimler
          </VTab>
          <VTab value="payment">
            <VIcon icon="tabler-clock-pause" size="18" class="me-1" />
            Ödeme
          </VTab>
          <VTab value="telegram">
            <VIcon icon="tabler-brand-telegram" size="18" class="me-1" />
            Telegram
          </VTab>
          <VTab value="ai">
            <VIcon icon="tabler-robot" size="18" class="me-1" />
            AI Doğrulama
          </VTab>
        </VTabs>

        <VWindow v-model="activeTab" class="pa-6">
          <!-- Bildirimler -->
          <VWindowItem value="notifications">
            <h6 class="text-body-1 font-weight-bold mb-2">{{ brand }} Hesap Uyarısı</h6>
            <p class="text-body-2 text-medium-emphasis mb-4">
              Aktif takım hesaplarından, bu tutarın yatırımını kabul edebilen (min ≤ tutar ≤ max) hesap kalmadığında {{ brand }} grubuna her dakika uyarı gönderilir.
            </p>
            <VRow>
              <VCol cols="12">
                <VSwitch
                  v-model="settings.payroute_alert_enabled"
                  :true-value="1" :false-value="0"
                  :label="brand + ' Uyarıları Aktif'"
                  color="primary"
                  density="compact"
                  hide-details
                />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="settings.min_notify_amount"
                  type="number"
                  label="Minimum Bildirim Tutarı"
                  prefix="₺"
                  hint="Örn: 100 — sorun varsa 5 dakikada bir uyarı"
                  persistent-hint
                  :disabled="!settings.payroute_alert_enabled"
                />
              </VCol>
            </VRow>
            <VBtn class="mt-6" color="primary" :loading="saving" @click="saveSettings">
              <VIcon start icon="tabler-device-floppy" /> Kaydet
            </VBtn>
          </VWindowItem>

          <!-- Ödeme -->
          <VWindowItem value="payment">
            <h6 class="text-body-1 font-weight-bold mb-2">Ödeme Linki Geçerlilik Süresi</h6>
            <p class="text-body-2 text-medium-emphasis mb-4">
              Açıkken: oyuncuya gönderilen pay link'i belirtilen dakika içinde ödenmezse otomatik reddedilir ve merchant'a "Ödeme bulunmadı" başarısız callback'i gönderilir.
            </p>
            <VRow>
              <VCol cols="12">
                <VSwitch
                  v-model="settings.pay_link_expiry_enabled"
                  :true-value="1" :false-value="0"
                  label="Link süresi sınırı aktif"
                  color="primary"
                  density="compact"
                  hide-details
                />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="settings.pay_link_expiry_minutes"
                  type="number"
                  label="Geçerlilik Süresi"
                  suffix="dakika"
                  min="1"
                  hint="Örn: 15 — link oluşturulduktan 15 dakika içinde ödenmezse otomatik reddedilir"
                  persistent-hint
                  :disabled="!settings.pay_link_expiry_enabled"
                />
              </VCol>
            </VRow>
            <VBtn class="mt-6" color="primary" :loading="saving" @click="saveSettings">
              <VIcon start icon="tabler-device-floppy" /> Kaydet
            </VBtn>
          </VWindowItem>

          <!-- Telegram -->
          <VWindowItem value="telegram">
            <h6 class="text-body-1 font-weight-bold mb-2">Telegram Bot Ayarları</h6>
            <p class="text-body-2 text-medium-emphasis mb-4">
              Bot token ve {{ brand }} bildirim grubunun chat ID'sini burada yönetin. Bot token'ı BotFather'dan, chat ID'yi ise aşağıdaki yardımcıyla yakalayabilirsiniz.
            </p>

            <VRow>
              <VCol cols="12" md="8">
                <AppTextField
                  v-model="settings.telegram_bot_token"
                  :type="showBotToken ? 'text' : 'password'"
                  label="Bot Token"
                  placeholder="123456789:ABCdefGhIJklmnoPQrsTuvwxyz"
                  :append-inner-icon="showBotToken ? 'tabler-eye-off' : 'tabler-eye'"
                  @click:append-inner="showBotToken = !showBotToken"
                  hint="BotFather'dan aldığınız token. Kaydedildikten sonra panel ve cron job'lar bu token'ı kullanır."
                  persistent-hint
                />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="settings.telegram_chat_id"
                  label="Chat ID"
                  placeholder="-1001234567890"
                  hint="Bildirimlerin gönderileceği grup/kanal ID'si"
                  persistent-hint
                />
              </VCol>
            </VRow>

            <VDivider class="my-6" />

            <h6 class="text-body-1 font-weight-bold mb-2">
              <VIcon icon="tabler-search" size="18" class="me-1" />
              Chat ID Bul
            </h6>
            <p class="text-body-2 text-medium-emphasis mb-3">
              1. Botu Telegram grubuna ekleyin. 2. Grupta herhangi bir mesaj yazın (örn. /start). 3. Aşağıya grup adının tamamını veya bir kısmını yazıp <strong>Chat ID Al</strong> butonuna basın.
            </p>

            <VCard variant="tonal" color="info" class="pa-4">
              <VRow align="center" no-gutters>
                <VCol>
                  <AppTextField
                    v-model="groupName"
                    label="Grup adı"
                    :placeholder="`Örn: ${brand} Bildirimleri`"
                    density="compact"
                    hide-details
                    @keyup.enter="findChatId"
                  />
                </VCol>
                <VCol cols="auto" class="ms-3">
                  <VBtn color="info" :loading="findingChatId" :disabled="!settings.telegram_bot_token" @click="findChatId">
                    <VIcon start icon="tabler-target" /> Chat ID Al
                  </VBtn>
                </VCol>
              </VRow>
              <div v-if="!settings.telegram_bot_token" class="text-caption text-warning mt-2">
                <VIcon icon="tabler-alert-triangle" size="14" /> Önce Bot Token girip kaydedin.
              </div>
            </VCard>

            <div v-if="chatIdMatches.length > 0" class="mt-4">
              <div class="text-body-2 font-weight-medium mb-2">{{ chatIdMatches.length }} eşleşme bulundu:</div>
              <VList class="rounded border">
                <VListItem
                  v-for="m in chatIdMatches"
                  :key="m.chat_id"
                  @click="useChatId(m.chat_id)"
                >
                  <template #prepend>
                    <VIcon :icon="m.type === 'channel' ? 'tabler-broadcast' : 'tabler-users'" size="20" />
                  </template>
                  <VListItemTitle class="font-weight-semibold">{{ m.title }}</VListItemTitle>
                  <VListItemSubtitle>
                    <code>{{ m.chat_id }}</code>
                    <span class="ms-2 text-medium-emphasis">{{ m.type }}</span>
                  </VListItemSubtitle>
                  <template #append>
                    <VBtn size="small" variant="tonal" color="primary">Kullan</VBtn>
                  </template>
                </VListItem>
              </VList>
            </div>

            <VAlert v-if="chatIdHint" type="warning" variant="tonal" density="compact" class="mt-4">
              {{ chatIdHint }}
            </VAlert>

            <VBtn class="mt-6" color="primary" :loading="saving" @click="saveSettings">
              <VIcon start icon="tabler-device-floppy" /> Kaydet
            </VBtn>
          </VWindowItem>

          <!-- AI Doğrulama -->
          <VWindowItem value="ai">
            <h6 class="text-body-1 font-weight-bold mb-2">Anthropic Claude Vision</h6>
            <p class="text-body-2 text-medium-emphasis mb-4">
              Çekim dekontları arka planda Claude Vision ile analiz edilir. Tutar/IBAN/alıcı eşleşmesi ve sahtelik belirtileri kontrol edilir.
              API key <a href="https://console.anthropic.com/settings/keys" target="_blank">console.anthropic.com</a> üzerinden alınır.
            </p>

            <VRow>
              <VCol cols="12" md="8">
                <AppTextField
                  v-model="settings.anthropic_api_key"
                  :type="anthropicKeySet ? 'password' : 'text'"
                  :placeholder="anthropicKeySet ? anthropicKeyMasked + ' (kayıtlı — değiştirmek için yeni key girin)' : 'sk-ant-...'"
                  label="API Key"
                  density="comfortable"
                  prepend-inner-icon="tabler-key"
                />
              </VCol>
              <VCol cols="12" md="4">
                <VSelect
                  v-model="settings.anthropic_vision_model"
                  :items="[
                    { title: 'Claude Haiku 4.5 (en ucuz, hızlı)', value: 'claude-haiku-4-5' },
                    { title: 'Claude Sonnet 4.6 (denge)', value: 'claude-sonnet-4-6' },
                    { title: 'Claude Opus 4.7 (en güçlü)', value: 'claude-opus-4-7' },
                  ]"
                  label="Model"
                  density="comfortable"
                />
              </VCol>
            </VRow>

            <div class="d-flex gap-2 mt-3 align-center">
              <VBtn color="primary" :loading="saving" prepend-icon="tabler-device-floppy" @click="saveSettings">Kaydet</VBtn>
              <VBtn variant="outlined" :loading="anthropicTesting" prepend-icon="tabler-plug-connected" @click="testAnthropicConnection">Bağlantıyı Test Et</VBtn>
              <VChip
                v-if="anthropicTestResult"
                :color="anthropicTestResult.ok ? 'success' : 'error'"
                size="small"
                label
              >{{ anthropicTestResult.message }}</VChip>
            </div>

            <VDivider class="my-6" />

            <!-- Bu ayki kullanım -->
            <h6 class="text-body-1 font-weight-bold mb-2">Bu Ayki Kullanım</h6>
            <p class="text-caption text-medium-emphasis mb-3">
              Tahmini maliyet; Anthropic'in fiyat listesine göre hesaplanır. Gerçek fatura için <a href="https://console.anthropic.com/settings/billing" target="_blank">console.anthropic.com/billing</a> sayfasını kontrol edin. (Anthropic bakiyesi normal API key ile dış sistemden okunamaz — admin key gerekli.)
            </p>
            <VRow v-if="anthropicUsage">
              <VCol cols="6" md="3">
                <VCard variant="tonal" color="info" class="pa-4 text-center">
                  <div class="text-caption text-medium-emphasis">Analiz</div>
                  <div class="text-h5 font-weight-bold">{{ anthropicUsage.analysis_count }}</div>
                </VCard>
              </VCol>
              <VCol cols="6" md="3">
                <VCard variant="tonal" color="primary" class="pa-4 text-center">
                  <div class="text-caption text-medium-emphasis">Input Token</div>
                  <div class="text-h5 font-weight-bold">{{ anthropicUsage.total_input_tokens.toLocaleString('tr-TR') }}</div>
                </VCard>
              </VCol>
              <VCol cols="6" md="3">
                <VCard variant="tonal" color="warning" class="pa-4 text-center">
                  <div class="text-caption text-medium-emphasis">Output Token</div>
                  <div class="text-h5 font-weight-bold">{{ anthropicUsage.total_output_tokens.toLocaleString('tr-TR') }}</div>
                </VCard>
              </VCol>
              <VCol cols="6" md="3">
                <VCard variant="tonal" color="success" class="pa-4 text-center">
                  <div class="text-caption text-medium-emphasis">Tahmini Maliyet</div>
                  <div class="text-h5 font-weight-bold">${{ Number(anthropicUsage.estimated_cost_usd).toFixed(3) }}</div>
                </VCard>
              </VCol>
              <VCol cols="12">
                <div class="text-caption text-medium-emphasis">
                  Mevcut model: <code>{{ anthropicUsage.current_model }}</code> · Dönem: {{ anthropicUsage.month }}
                </div>
              </VCol>
            </VRow>

            <VDivider class="my-6" />

            <!-- Manuel Dekont Test -->
            <h6 class="text-body-1 font-weight-bold mb-2">Manuel Dekont Testi</h6>
            <p class="text-caption text-medium-emphasis mb-3">
              Bir dekont yükleyip AI analizini gerçek bir çekim olmadan test edebilirsiniz. Sonuç DB'ye kaydedilmez.
            </p>

            <VRow dense>
              <VCol cols="12">
                <div
                  class="ai-test-dropzone d-flex align-center justify-center pa-4"
                  @click="testFileRef?.click()"
                >
                  <input
                    ref="testFileRef"
                    type="file"
                    accept="application/pdf,image/jpeg,image/png,image/webp"
                    class="d-none"
                    @change="onTestFileSelect"
                  />
                  <div v-if="testForm.file_name" class="text-body-2">
                    <VIcon icon="tabler-file-check" color="success" class="me-1" />
                    {{ testForm.file_name }} · {{ testForm.mime_type }}
                  </div>
                  <div v-else class="text-center">
                    <VIcon icon="tabler-cloud-upload" size="28" color="primary" />
                    <div class="text-body-2 mt-1">PDF / JPG / PNG / WEBP — max 10 MB</div>
                  </div>
                </div>
              </VCol>

              <VCol cols="12" md="4">
                <AppTextField v-model="testForm.amount" type="number" label="Beklenen Tutar (₺)" placeholder="örn 5000" density="compact" />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField v-model="testForm.iban" label="Beklenen IBAN" placeholder="TR12 ... 1234" density="compact" />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField v-model="testForm.recipient" label="Beklenen Alıcı" placeholder="Ahmet Yılmaz" density="compact" />
              </VCol>

              <VCol cols="12">
                <VBtn
                  color="primary"
                  :loading="testAnalyzing"
                  :disabled="!testForm.file_base64"
                  prepend-icon="tabler-robot"
                  @click="analyzeManualReceipt"
                >Analiz Et</VBtn>
              </VCol>

              <VCol v-if="testResult" cols="12">
                <VCard variant="outlined" class="mt-2">
                  <VCardItem>
                    <VCardTitle class="d-flex align-center justify-space-between">
                      <span>AI Sonucu</span>
                      <VChip color="info" size="small" label>~${{ Number(testResult.estimated_cost_usd).toFixed(5) }}</VChip>
                    </VCardTitle>
                  </VCardItem>
                  <VDivider />
                  <VCardText>
                    <table class="ai-test-table">
                      <tr><th>Dekont mu?</th><td>{{ testResult.result.is_receipt ? '✓ Evet' : '✗ Hayır' }}</td></tr>
                      <tr><th>Tutar</th><td>{{ testResult.result.amount !== null ? '₺' + Number(testResult.result.amount).toLocaleString('tr-TR', {minimumFractionDigits: 2}) : '—' }}</td></tr>
                      <tr><th>IBAN son 4</th><td><code v-if="testResult.result.iban_last4">{{ testResult.result.iban_last4 }}</code><span v-else>—</span></td></tr>
                      <tr><th>Alıcı</th><td>{{ testResult.result.recipient_name || '—' }}</td></tr>
                      <tr v-if="testResult.result.sender_name"><th>Gönderen</th><td>{{ testResult.result.sender_name }}</td></tr>
                      <tr><th>Banka</th><td>{{ testResult.result.bank_name || '—' }}</td></tr>
                      <tr v-if="testResult.result.transaction_date"><th>İşlem zamanı</th><td>{{ testResult.result.transaction_date }}</td></tr>
                      <tr v-if="testResult.result.transaction_id"><th>Ref no</th><td><code>{{ testResult.result.transaction_id }}</code></td></tr>
                      <tr><th>Güven</th><td>{{ testResult.result.confidence }}/100</td></tr>
                      <tr><th>Manipülasyon belirtisi</th><td>{{ testResult.result.signs_of_tampering ? '⚠️ Evet' : '✓ Hayır' }}</td></tr>
                    </table>
                    <div v-if="testResult.result.notes" class="text-caption text-medium-emphasis mt-2" style="white-space: pre-line;">
                      <strong>AI Notu:</strong> {{ testResult.result.notes }}
                    </div>
                    <div v-if="testResult.result._usage" class="text-caption text-medium-emphasis mt-2">
                      {{ testResult.result._usage.model }} · {{ testResult.result._usage.input_tokens }} in / {{ testResult.result._usage.output_tokens }} out token
                    </div>
                  </VCardText>
                </VCard>
              </VCol>
            </VRow>
          </VWindowItem>
        </VWindow>
      </VCard>
    </VCol>
  </VRow>
</template>

<style scoped>
.ai-test-dropzone {
  border: 2px dashed rgba(var(--v-border-color), 0.4);
  border-radius: 8px;
  cursor: pointer;
  background: rgba(var(--v-theme-on-surface), 0.02);
  min-height: 80px;
  transition: all .15s ease;
}
.ai-test-dropzone:hover {
  border-color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.05);
}
.ai-test-table { width: 100%; border-collapse: collapse; }
.ai-test-table th { text-align: left; padding: 6px 10px; font-weight: 500; color: rgba(var(--v-theme-on-surface), 0.65); width: 35%; border-bottom: 1px solid rgba(var(--v-border-color), 0.1); }
.ai-test-table td { padding: 6px 10px; border-bottom: 1px solid rgba(var(--v-border-color), 0.1); }
.ai-test-table code { font-family: monospace; }
</style>
