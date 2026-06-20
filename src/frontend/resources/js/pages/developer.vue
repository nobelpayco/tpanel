<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import LanguageSwitcher from '@/components/LanguageSwitcher.vue'
import { getBrand } from '@/composables/useBrand'

const brand = getBrand()

definePage({
  meta: {
    layout: 'blank',
    public: true,
  },
})

const { t } = useI18n()

const sections = [
  { id: 'overview',    icon: 'tabler-info-circle',   key: 'overview' },
  { id: 'auth',        icon: 'tabler-key',           key: 'auth' },
  { id: 'deposit',     icon: 'tabler-arrow-down',    key: 'deposit',     method: 'POST', path: '/api/v1/deposit' },
  { id: 'deposit_h2h', icon: 'tabler-bolt',          key: 'deposit_h2h', method: 'POST', path: '/api/v1/deposit/direct' },
  { id: 'withdraw',    icon: 'tabler-arrow-up',      key: 'withdraw',    method: 'POST', path: '/api/v1/withdraw' },
  { id: 'transaction', icon: 'tabler-search',        key: 'transaction', method: 'GET',  path: '/api/v1/transaction/{order_id}' },
  { id: 'callback',    icon: 'tabler-webhook',       key: 'callback' },
  { id: 'errors',      icon: 'tabler-alert-circle',  key: 'errors' },
  { id: 'postman',     icon: 'tabler-brand-postman', key: 'postman' },
]

const activeId = ref('overview')
const baseUrl = computed(() => `${window.location.origin}/api/v1`)

const scrollTo = (id) => {
  const el = document.getElementById(id)
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

// Scrollspy
let observer = null
onMounted(() => {
  observer = new IntersectionObserver(
    (entries) => {
      const visible = entries.filter(e => e.isIntersecting)
      if (visible.length) activeId.value = visible[0].target.id
    },
    { rootMargin: '-30% 0px -60% 0px', threshold: 0 },
  )
  sections.forEach(s => {
    const el = document.getElementById(s.id)
    if (el) observer.observe(el)
  })
})
onUnmounted(() => observer?.disconnect())

const methodColor = (m) => ({ POST: 'success', GET: 'info', PUT: 'warning', DELETE: 'error' }[m] || 'primary')

// Copy
const copiedKey = ref('')
const copy = (text, key) => {
  navigator.clipboard?.writeText(text)
  copiedKey.value = key
  setTimeout(() => { if (copiedKey.value === key) copiedKey.value = '' }, 1500)
}

// Code examples
const codeLang = ref('php')
const codeLangs = [
  { value: 'php',  label: 'PHP' },
  { value: 'curl', label: 'cURL' },
  { value: 'node', label: 'Node.js' },
]

const baseUrlStr = computed(() => baseUrl.value)

const codeSign = computed(() => {
  const BASE = baseUrlStr.value
  if (codeLang.value === 'php') {
    return `<?php
$apiKey    = 'YOUR_API_KEY';
$apiSecret = 'YOUR_API_SECRET';
$method    = 'POST';
$path      = '/api/v1/deposit';
$timestamp = (string) time();
$body      = json_encode([
    'order_id'     => 'ORDER-001',
    'amount'       => 1500,
    'player_id'    => 'u12345',
    'name'         => 'Ahmet Yılmaz',
    'callback_url' => 'https://merchant.com/webhook',
]);

$bodyHash      = hash('sha256', $body);
$signedPayload = "$method\\n$path\\n$timestamp\\n$bodyHash";
$signature     = hash_hmac('sha256', $signedPayload, $apiSecret);

$ch = curl_init('${BASE}/deposit');
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_POST           => true,
    CURLOPT_POSTFIELDS     => $body,
    CURLOPT_HTTPHEADER     => [
        'Content-Type: application/json',
        'X-Api-Key: '   . $apiKey,
        'X-Timestamp: ' . $timestamp,
        'X-Signature: ' . $signature,
    ],
]);
echo curl_exec($ch);
curl_close($ch);`
  }
  if (codeLang.value === 'curl') {
    return `BASE_URL="${BASE}"
API_KEY="YOUR_API_KEY"
API_SECRET="YOUR_API_SECRET"
METHOD="POST"
PATH_INFO="/api/v1/deposit"
TIMESTAMP=$(date +%s)
BODY='{"order_id":"ORDER-001","amount":1500,"player_id":"u12345","name":"Ahmet","callback_url":"https://merchant.com/webhook"}'

BODY_HASH=$(printf %s "$BODY" | sha256sum | awk '{print $1}')
SIGNED="$METHOD\\n$PATH_INFO\\n$TIMESTAMP\\n$BODY_HASH"
SIG=$(printf %b "$SIGNED" | openssl dgst -sha256 -hmac "$API_SECRET" | awk '{print $2}')

curl -X POST "$BASE_URL/deposit" \\
  -H "Content-Type: application/json" \\
  -H "X-Api-Key: $API_KEY" \\
  -H "X-Timestamp: $TIMESTAMP" \\
  -H "X-Signature: $SIG" \\
  -d "$BODY"`
  }
  return `import crypto from 'crypto'

const apiKey    = 'YOUR_API_KEY'
const apiSecret = 'YOUR_API_SECRET'
const method    = 'POST'
const path      = '/api/v1/deposit'
const timestamp = Math.floor(Date.now() / 1000).toString()
const body = JSON.stringify({
  order_id: 'ORDER-001',
  amount: 1500,
  player_id: 'u12345',
  name: 'Ahmet Yılmaz',
  callback_url: 'https://merchant.com/webhook',
})

const bodyHash  = crypto.createHash('sha256').update(body).digest('hex')
const signed    = \`\${method}\\n\${path}\\n\${timestamp}\\n\${bodyHash}\`
const signature = crypto.createHmac('sha256', apiSecret).update(signed).digest('hex')

const res = await fetch('${BASE}/deposit', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Api-Key':    apiKey,
    'X-Timestamp':  timestamp,
    'X-Signature':  signature,
  },
  body,
})
console.log(await res.json())`
})

// Response examples
const respDeposit = `{
  "code": 200,
  "status": true,
  "message": "Yatırım talebi oluşturuldu.",
  "data": {
    "transaction_id": 12345,
    "order_id": "ORDER-001",
    "u_id": "abc123def456...",
    "amount": 1500,
    "pay_url": "${baseUrlStr.value.replace('/api/v1', '')}/pay/abc123def456..."
  }
}`

const respWithdraw = `{
  "code": 200,
  "status": true,
  "message": "Çekim talebi oluşturuldu.",
  "data": {
    "transaction_id": 12346,
    "order_id": "WD-001",
    "u_id": "xyz789...",
    "amount": 2500,
    "status": "pending"
  }
}`

const respTransaction = `{
  "code": 200,
  "status": true,
  "data": {
    "order_id": "ORDER-001",
    "u_id": "abc123...",
    "status": "approved",
    "status_code": 3,
    "type": "deposit",
    "amount": 1500,
    "name": "Ahmet Yılmaz",
    "player_id": "u12345",
    "created_at": "2026-05-18 14:23:11",
    "finalized_at": "2026-05-18 14:31:02"
  }
}`

const sigFormula = `signed_payload = METHOD + "\\n" + PATH + "\\n" + TIMESTAMP + "\\n" + sha256(BODY)
signature      = hex( hmac_sha256(apiSecret, signed_payload) )`

const reqDeposit = `{
  "order_id":           "ORDER-001",
  "amount":             1500,
  "player_id":          "u12345",
  "name":               "Ahmet Yılmaz",
  "callback_url":       "https://merchant.com/webhook",
  "successRedirectUrl": "https://merchant.com/payment/success",
  "failRedirectUrl":    "https://merchant.com/payment/fail"
}`

const reqDepositH2H = `{
  "order_id":     "ORDER-H2H-001",
  "amount":       1500,
  "player_id":    "u12345",
  "name":         "Ahmet Yılmaz",
  "callback_url": "https://merchant.com/webhook"
}`

const respDepositH2H = `{
  "code": 200,
  "status": true,
  "message": "Deposit request created (H2H).",
  "data": {
    "transaction_id": 12347,
    "order_id": "ORDER-H2H-001",
    "u_id": "h2h0123...",
    "amount": 1500,
    "bank": {
      "id": 42,
      "account_holder": "Test Hesap",
      "account_iban": "TR12 0006 4000 0011 2345 6789 01",
      "bank_name": "Akbank"
    }
  }
}`

const reqWithdraw = `{
  "order_id":     "WD-001",
  "amount":       2500,
  "player_id":    "u12345",
  "name":         "Ahmet Yılmaz",
  "iban":         "TR000000000000000000000001",
  "callback_url": "https://merchant.com/webhook"
}`

const callbackApproved = `// POST {callback_url}
// Headers: Content-Type: application/json
{
  "code":       200,
  "status":     true,
  "uID":        "ORDER-001",
  "saleID":     "abc123...",
  "amount":     1500,
  "senderName": "Ahmet Yılmaz",
  "hash":       "sha256(apiKey | order_id | 'true')",
  "message":    "Ödeme onaylandı"
}`

// Postman Collection v2.1 — HMAC imzayı otomatik üreten pre-request script ile
const postmanCollection = computed(() => {
  const preReq = [
    "const apiKey    = pm.collectionVariables.get('api_key');",
    "const apiSecret = pm.collectionVariables.get('api_secret');",
    "if (!apiKey || !apiSecret) { console.warn('api_key/api_secret collection variable boş'); return; }",
    "",
    "// URL ve body içindeki değişkenleri tam çöz (örn {{base_url}}, {{order_id}})",
    "const fullUrl   = pm.variables.replaceIn(pm.request.url.toString());",
    "// pathname'i alıp çift slash'ları teke indir, sonda slash varsa kaldır",
    "let path = '';",
    "try { path = new URL(fullUrl).pathname; }",
    "catch (e) { console.error('URL parse hatası — base_url tam mı?', fullUrl, e); return; }",
    "path = path.replace(/\\/+/g, '/').replace(/\\/$/, '') || '/';",
    "",
    "const method    = pm.request.method;",
    "const timestamp = Math.floor(Date.now() / 1000).toString();",
    "let body = '';",
    "if (pm.request.body && pm.request.body.raw) {",
    "    body = pm.variables.replaceIn(pm.request.body.raw);",
    "}",
    "",
    "const bodyHash  = CryptoJS.SHA256(body).toString();",
    "const signed    = method + '\\n' + path + '\\n' + timestamp + '\\n' + bodyHash;",
    "const signature = CryptoJS.HmacSHA256(signed, apiSecret).toString();",
    "",
    "// Debug — Postman Console'da imza üretiminin görünmesi için",
    "console.log('[HMAC] method=' + method + ' path=' + path + ' ts=' + timestamp);",
    "console.log('[HMAC] bodyHash=' + bodyHash);",
    "console.log('[HMAC] signedPayload=' + JSON.stringify(signed));",
    "console.log('[HMAC] signature=' + signature);",
    "",
    "pm.request.headers.upsert({ key: 'X-Api-Key',    value: apiKey });",
    "pm.request.headers.upsert({ key: 'X-Timestamp',  value: timestamp });",
    "pm.request.headers.upsert({ key: 'X-Signature',  value: signature });",
    "pm.request.headers.upsert({ key: 'Content-Type', value: 'application/json' });",
  ]
  const collection = {
    info: {
      name: `${brand} Merchant API v1`,
      description: 'HMAC-signed merchant integration API. apiKey ve apiSecret değerlerini Collection Variables içine girin; imza her istekte otomatik hesaplanır.',
      schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
    },
    variable: [
      { key: 'base_url',   value: baseUrlStr.value, type: 'string' },
      { key: 'api_key',    value: '',                type: 'string' },
      { key: 'api_secret', value: '',                type: 'string' },
    ],
    event: [
      { listen: 'prerequest', script: { type: 'text/javascript', exec: preReq } },
    ],
    item: [
      {
        name: 'Create Deposit',
        request: {
          method: 'POST',
          header: [],
          url: { raw: '{{base_url}}/deposit', host: ['{{base_url}}'], path: ['deposit'] },
          body: { mode: 'raw', raw: reqDeposit, options: { raw: { language: 'json' } } },
        },
      },
      {
        name: 'Create Deposit (H2H)',
        request: {
          method: 'POST',
          header: [],
          url: { raw: '{{base_url}}/deposit/direct', host: ['{{base_url}}'], path: ['deposit', 'direct'] },
          body: { mode: 'raw', raw: reqDepositH2H, options: { raw: { language: 'json' } } },
        },
      },
      {
        name: 'Create Withdraw',
        request: {
          method: 'POST',
          header: [],
          url: { raw: '{{base_url}}/withdraw', host: ['{{base_url}}'], path: ['withdraw'] },
          body: { mode: 'raw', raw: reqWithdraw, options: { raw: { language: 'json' } } },
        },
      },
      {
        name: 'Transaction Status',
        request: {
          method: 'GET',
          header: [],
          url: { raw: '{{base_url}}/transaction/ORDER-001', host: ['{{base_url}}'], path: ['transaction', 'ORDER-001'] },
        },
      },
    ],
  }
  return JSON.stringify(collection, null, 2)
})

const downloadPostman = () => {
  const blob = new Blob([postmanCollection.value], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${brand.toLowerCase()}-api-v1.postman_collection.json`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

const callbackVerify = `<?php
$payload = json_decode(file_get_contents('php://input'), true);

$expected = hash('sha256',
    $apiKey . '|' . $payload['uID'] . '|' . ($payload['status'] ? 'true' : 'false')
);

if (!hash_equals($expected, $payload['hash'])) {
    http_response_code(401);
    exit('Invalid signature');
}

// ... order'ı approved/rejected olarak işaretleyin ...

http_response_code(200);
echo 'OK';`
</script>

<template>
  <div class="dev-docs">
    <header class="dev-header">
      <div class="dev-header-inner">
        <div class="d-flex align-center gap-3">
          <div class="dev-logo">
            <VIcon icon="tabler-code" size="20" />
          </div>
          <div>
            <div class="text-h6 font-weight-bold lh-1">{{ brand }} API</div>
            <div class="text-caption text-medium-emphasis">v1 · Developer Reference</div>
          </div>
        </div>
        <LanguageSwitcher />
      </div>
    </header>

    <div class="dev-body">
      <!-- Sidebar -->
      <aside class="dev-sidebar">
        <div class="text-overline text-medium-emphasis mb-2 px-2">Reference</div>
        <nav>
          <a
            v-for="s in sections"
            :key="s.id"
            class="dev-nav-link"
            :class="{ active: activeId === s.id }"
            @click.prevent="scrollTo(s.id)"
          >
            <VIcon :icon="s.icon" size="18" class="me-2" />
            <span>{{ t('developer.sections.' + s.key + '.title') }}</span>
            <VChip v-if="s.method" :color="methodColor(s.method)" size="x-small" label class="ms-auto method-chip">
              {{ s.method }}
            </VChip>
          </a>
        </nav>
        <div class="dev-baseurl">
          <div class="text-caption text-medium-emphasis mb-1">Base URL</div>
          <div class="dev-baseurl-code">
            <code>{{ baseUrl }}</code>
            <VBtn icon size="x-small" variant="text" @click="copy(baseUrl, 'baseurl')">
              <VIcon :icon="copiedKey === 'baseurl' ? 'tabler-check' : 'tabler-copy'" size="14" />
            </VBtn>
          </div>
        </div>
      </aside>

      <!-- Content -->
      <main class="dev-content">
        <!-- Overview -->
        <section id="overview" class="doc-section">
          <h1 class="doc-h1">{{ t('developer.title', { brand }) }}</h1>
          <p class="doc-lead">{{ t('developer.sections.overview.body', { brand }) }}</p>
        </section>

        <!-- Auth -->
        <section id="auth" class="doc-section">
          <h2 class="doc-h2">{{ t('developer.sections.auth.title') }}</h2>
          <p class="doc-p">{{ t('developer.sections.auth.body') }}</p>

          <h3 class="doc-h3">{{ t('developer.sections.auth.headers_title') }}</h3>
          <div class="param-table">
            <div class="param-row" v-for="h in [
              { name: 'X-Api-Key',    desc: t('developer.sections.auth.h_api_key') },
              { name: 'X-Timestamp',  desc: t('developer.sections.auth.h_timestamp') },
              { name: 'X-Signature',  desc: t('developer.sections.auth.h_signature') },
              { name: 'Content-Type', desc: 'application/json' },
            ]" :key="h.name">
              <div class="param-name"><code>{{ h.name }}</code></div>
              <div class="param-desc">{{ h.desc }}</div>
            </div>
          </div>

          <h3 class="doc-h3 mt-6">{{ t('developer.sections.auth.signature_title') }}</h3>
          <p class="doc-p">{{ t('developer.sections.auth.signature_body') }}</p>
          <div class="code-card mb-6">
            <div class="code-card-header">
              <span class="code-lang">formula</span>
              <VBtn icon size="x-small" variant="text" color="white" @click="copy(sigFormula, 'sig-formula')">
                <VIcon :icon="copiedKey === 'sig-formula' ? 'tabler-check' : 'tabler-copy'" size="14" />
              </VBtn>
            </div>
            <pre class="code-body"><code>{{ sigFormula }}</code></pre>
          </div>

          <h3 class="doc-h3">{{ t('developer.sections.auth.example_title') }}</h3>
          <div class="code-card">
            <div class="code-card-header">
              <div class="d-flex gap-1">
                <button
                  v-for="l in codeLangs"
                  :key="l.value"
                  class="lang-tab"
                  :class="{ active: codeLang === l.value }"
                  @click="codeLang = l.value"
                >{{ l.label }}</button>
              </div>
              <VBtn icon size="x-small" variant="text" color="white" @click="copy(codeSign, 'code-sign')">
                <VIcon :icon="copiedKey === 'code-sign' ? 'tabler-check' : 'tabler-copy'" size="14" />
              </VBtn>
            </div>
            <pre class="code-body"><code>{{ codeSign }}</code></pre>
          </div>
        </section>

        <!-- Deposit -->
        <section id="deposit" class="doc-section endpoint-section">
          <div class="endpoint-head">
            <VChip color="success" size="small" label class="method-chip-lg">POST</VChip>
            <code class="endpoint-path">/api/v1/deposit</code>
          </div>
          <h2 class="doc-h2 mt-2">{{ t('developer.sections.deposit.title') }}</h2>

          <div class="endpoint-grid">
            <div>
              <p class="doc-p">{{ t('developer.sections.deposit.body') }}</p>

              <h3 class="doc-h3">{{ t('developer.body_params') }}</h3>
              <div class="param-table">
                <div class="param-row">
                  <div class="param-name"><code>order_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_order_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>amount</code> <span class="param-meta">number · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_amount') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>player_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_player_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>name</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_name') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>callback_url</code> <span class="param-meta">url · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_callback') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>successRedirectUrl</code> <span class="param-meta">url · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_success_url') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>failRedirectUrl</code> <span class="param-meta">url · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_fail_url') }}</div>
                </div>
              </div>
              <p class="doc-note">{{ t('developer.sections.deposit.response_note') }}</p>
            </div>

            <div class="endpoint-examples">
              <div class="code-card">
                <div class="code-card-header">
                  <span class="code-lang">request body</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(reqDeposit, 'req-dep')">
                    <VIcon :icon="copiedKey === 'req-dep' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ reqDeposit }}</code></pre>
              </div>
              <div class="code-card mt-3">
                <div class="code-card-header">
                  <span class="code-lang">200 · response</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(respDeposit, 'resp-dep')">
                    <VIcon :icon="copiedKey === 'resp-dep' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ respDeposit }}</code></pre>
              </div>
            </div>
          </div>
        </section>

        <!-- Deposit H2H -->
        <section id="deposit_h2h" class="doc-section endpoint-section">
          <div class="endpoint-head">
            <VChip color="success" size="small" label class="method-chip-lg">POST</VChip>
            <code class="endpoint-path">/api/v1/deposit/direct</code>
          </div>
          <h2 class="doc-h2 mt-2">{{ t('developer.sections.deposit_h2h.title') }}</h2>

          <div class="endpoint-grid">
            <div>
              <p class="doc-p">{{ t('developer.sections.deposit_h2h.body') }}</p>

              <h3 class="doc-h3">{{ t('developer.body_params') }}</h3>
              <div class="param-table">
                <div class="param-row">
                  <div class="param-name"><code>order_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_order_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>amount</code> <span class="param-meta">number · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_amount') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>player_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_player_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>name</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_name') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>callback_url</code> <span class="param-meta">url · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_callback') }}</div>
                </div>
              </div>
              <p class="doc-note">{{ t('developer.sections.deposit_h2h.response_note') }}</p>
            </div>

            <div class="endpoint-examples">
              <div class="code-card">
                <div class="code-card-header">
                  <span class="code-lang">request body</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(reqDepositH2H, 'req-h2h')">
                    <VIcon :icon="copiedKey === 'req-h2h' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ reqDepositH2H }}</code></pre>
              </div>
              <div class="code-card mt-3">
                <div class="code-card-header">
                  <span class="code-lang">200 · response</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(respDepositH2H, 'resp-h2h')">
                    <VIcon :icon="copiedKey === 'resp-h2h' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ respDepositH2H }}</code></pre>
              </div>
            </div>
          </div>
        </section>

        <!-- Withdraw -->
        <section id="withdraw" class="doc-section endpoint-section">
          <div class="endpoint-head">
            <VChip color="success" size="small" label class="method-chip-lg">POST</VChip>
            <code class="endpoint-path">/api/v1/withdraw</code>
          </div>
          <h2 class="doc-h2 mt-2">{{ t('developer.sections.withdraw.title') }}</h2>

          <div class="endpoint-grid">
            <div>
              <p class="doc-p">{{ t('developer.sections.withdraw.body') }}</p>

              <h3 class="doc-h3">{{ t('developer.body_params') }}</h3>
              <div class="param-table">
                <div class="param-row">
                  <div class="param-name"><code>order_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_order_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>amount</code> <span class="param-meta">number · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.withdraw.f_amount') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>player_id</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_player_id') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>name</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_name') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>iban</code> <span class="param-meta">string · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.withdraw.f_iban') }}</div>
                </div>
                <div class="param-row">
                  <div class="param-name"><code>callback_url</code> <span class="param-meta">url · {{ t('developer.required') }}</span></div>
                  <div class="param-desc">{{ t('developer.sections.deposit.f_callback') }}</div>
                </div>
              </div>
            </div>

            <div class="endpoint-examples">
              <div class="code-card">
                <div class="code-card-header">
                  <span class="code-lang">request body</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(reqWithdraw, 'req-wd')">
                    <VIcon :icon="copiedKey === 'req-wd' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ reqWithdraw }}</code></pre>
              </div>
              <div class="code-card mt-3">
                <div class="code-card-header">
                  <span class="code-lang">200 · response</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(respWithdraw, 'resp-wd')">
                    <VIcon :icon="copiedKey === 'resp-wd' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ respWithdraw }}</code></pre>
              </div>
            </div>
          </div>
        </section>

        <!-- Transaction -->
        <section id="transaction" class="doc-section endpoint-section">
          <div class="endpoint-head">
            <VChip color="info" size="small" label class="method-chip-lg">GET</VChip>
            <code class="endpoint-path">/api/v1/transaction/{order_id}</code>
          </div>
          <h2 class="doc-h2 mt-2">{{ t('developer.sections.transaction.title') }}</h2>

          <div class="endpoint-grid">
            <div>
              <p class="doc-p">{{ t('developer.sections.transaction.body') }}</p>

              <h3 class="doc-h3">{{ t('developer.sections.transaction.states_title') }}</h3>
              <p class="doc-p">{{ t('developer.sections.transaction.states_intro') }}</p>

              <div class="status-grid">
                <div class="status-card status-pending">
                  <div class="status-card-head">
                    <VChip color="warning" size="x-small" label>pending</VChip>
                    <span class="status-state-tag">{{ t('developer.sections.transaction.state_pending_title') }}</span>
                  </div>
                  <p class="status-card-body">{{ t('developer.sections.transaction.state_pending_desc') }}</p>
                </div>

                <div class="status-card status-processing">
                  <div class="status-card-head">
                    <VChip color="info" size="x-small" label>processing</VChip>
                    <span class="status-state-tag">{{ t('developer.sections.transaction.state_processing_title') }}</span>
                  </div>
                  <p class="status-card-body">{{ t('developer.sections.transaction.state_processing_desc') }}</p>
                </div>

                <div class="status-card status-approved">
                  <div class="status-card-head">
                    <VChip color="success" size="x-small" label>approved</VChip>
                    <span class="status-state-tag">{{ t('developer.sections.transaction.state_approved_title') }}</span>
                  </div>
                  <p class="status-card-body">{{ t('developer.sections.transaction.state_approved_desc') }}</p>
                </div>

                <div class="status-card status-rejected">
                  <div class="status-card-head">
                    <VChip color="error" size="x-small" label>rejected</VChip>
                    <span class="status-state-tag">{{ t('developer.sections.transaction.state_rejected_title') }}</span>
                  </div>
                  <p class="status-card-body">{{ t('developer.sections.transaction.state_rejected_desc') }}</p>
                </div>
              </div>

              <p class="doc-note">{{ t('developer.sections.transaction.polling_tip') }}</p>
            </div>
            <div class="endpoint-examples">
              <div class="code-card">
                <div class="code-card-header">
                  <span class="code-lang">200 · response</span>
                  <VBtn icon size="x-small" variant="text" color="white" @click="copy(respTransaction, 'resp-tx')">
                    <VIcon :icon="copiedKey === 'resp-tx' ? 'tabler-check' : 'tabler-copy'" size="14" />
                  </VBtn>
                </div>
                <pre class="code-body"><code>{{ respTransaction }}</code></pre>
              </div>
            </div>
          </div>
        </section>

        <!-- Callback -->
        <section id="callback" class="doc-section">
          <h2 class="doc-h2">{{ t('developer.sections.callback.title') }}</h2>
          <p class="doc-p">{{ t('developer.sections.callback.body', { brand }) }}</p>

          <h3 class="doc-h3">{{ t('developer.sections.callback.outcomes_title') }}</h3>
          <div class="status-grid mb-4">
            <div class="status-card status-approved">
              <div class="status-card-head">
                <VChip color="success" size="x-small" label>200 · approved</VChip>
                <span class="status-state-tag">{{ t('developer.sections.callback.outcome_approved_title') }}</span>
              </div>
              <p class="status-card-body">{{ t('developer.sections.callback.outcome_approved_desc') }}</p>
            </div>
            <div class="status-card status-rejected">
              <div class="status-card-head">
                <VChip color="error" size="x-small" label>201 · rejected</VChip>
                <span class="status-state-tag">{{ t('developer.sections.callback.outcome_rejected_title') }}</span>
              </div>
              <p class="status-card-body">{{ t('developer.sections.callback.outcome_rejected_desc') }}</p>
            </div>
          </div>
          <p class="doc-note">{{ t('developer.sections.callback.outcome_pending_note') }}</p>

          <div class="code-card mb-3">
            <div class="code-card-header">
              <span class="code-lang">approved payload</span>
              <VBtn icon size="x-small" variant="text" color="white" @click="copy(callbackApproved, 'cb-approved')">
                <VIcon :icon="copiedKey === 'cb-approved' ? 'tabler-check' : 'tabler-copy'" size="14" />
              </VBtn>
            </div>
            <pre class="code-body"><code>{{ callbackApproved }}</code></pre>
          </div>

          <div class="code-card">
            <div class="code-card-header">
              <span class="code-lang">php · verify hash</span>
              <VBtn icon size="x-small" variant="text" color="white" @click="copy(callbackVerify, 'cb-verify')">
                <VIcon :icon="copiedKey === 'cb-verify' ? 'tabler-check' : 'tabler-copy'" size="14" />
              </VBtn>
            </div>
            <pre class="code-body"><code>{{ callbackVerify }}</code></pre>
          </div>
        </section>

        <!-- Errors -->
        <section id="errors" class="doc-section">
          <h2 class="doc-h2">{{ t('developer.sections.errors.title') }}</h2>
          <p class="doc-p">{{ t('developer.sections.errors.body') }}</p>

          <div class="param-table">
            <div class="param-row" v-for="e in [
              { code: 200, color: 'success', key: 'h_200' },
              { code: 401, color: 'warning', key: 'h_401' },
              { code: 403, color: 'warning', key: 'h_403' },
              { code: 404, color: 'warning', key: 'h_404' },
              { code: 409, color: 'warning', key: 'h_409' },
              { code: 410, color: 'warning', key: 'h_410' },
              { code: 422, color: 'warning', key: 'h_422' },
              { code: 500, color: 'error',   key: 'h_500' },
            ]" :key="e.code">
              <div class="param-name">
                <VChip :color="e.color" size="x-small" label>{{ e.code }}</VChip>
              </div>
              <div class="param-desc">{{ t('developer.sections.errors.' + e.key) }}</div>
            </div>
          </div>
        </section>

        <!-- Postman -->
        <section id="postman" class="doc-section">
          <h2 class="doc-h2">{{ t('developer.sections.postman.title') }}</h2>
          <p class="doc-p">{{ t('developer.sections.postman.body') }}</p>

          <VBtn color="primary" prepend-icon="tabler-download" class="mb-4" @click="downloadPostman">
            {{ t('developer.sections.postman.download') }}
          </VBtn>

          <div class="code-card">
            <div class="code-card-header">
              <span class="code-lang">postman_collection.json</span>
              <VBtn icon size="x-small" variant="text" color="white" @click="copy(postmanCollection, 'postman-json')">
                <VIcon :icon="copiedKey === 'postman-json' ? 'tabler-check' : 'tabler-copy'" size="14" />
              </VBtn>
            </div>
            <pre class="code-body postman-pre"><code>{{ postmanCollection }}</code></pre>
          </div>
        </section>

        <div class="dev-footer">© {{ brand }} API · v1</div>
      </main>
    </div>
  </div>
</template>

<style scoped>
.dev-docs {
  min-height: 100vh;
  background: rgb(var(--v-theme-background));
  color: rgb(var(--v-theme-on-background));
}

/* Tüm code stillerini global override (prism vs Vuetify global CSS'i geç) */
.dev-docs :deep(code) {
  font-family: ui-monospace, "SF Mono", "JetBrains Mono", Menlo, Monaco, "Cascadia Code", monospace !important;
  font-size: 0.88em !important;
  font-weight: 500 !important;
  color: rgb(var(--v-theme-on-surface)) !important;
  background: rgba(var(--v-theme-on-surface), 0.07) !important;
  padding: 2px 6px !important;
  border-radius: 4px !important;
  border: none !important;
  text-shadow: none !important;
}
/* Koyu kod kartı içinde reset (pre kendi padding'ini korur) */
.dev-docs :deep(.code-body) {
  background: transparent !important;
  color: #c9d1d9 !important;
}
.dev-docs :deep(.code-body code) {
  background: transparent !important;
  color: #c9d1d9 !important;
  padding: 0 !important;
  font-weight: 400 !important;
  border-radius: 0 !important;
  font-size: inherit !important;
}

.dev-header {
  position: sticky;
  top: 0;
  z-index: 50;
  background: rgba(var(--v-theme-surface), 0.85);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}
.dev-header-inner {
  max-width: 1320px;
  margin: 0 auto;
  padding: 12px 32px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.dev-logo {
  width: 36px;
  height: 36px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, rgb(var(--v-theme-primary)) 0%, #5a4dd6 100%);
  color: white;
  border-radius: 10px;
}
.lh-1 { line-height: 1.1; }

.dev-body {
  display: grid;
  grid-template-columns: 260px 1fr;
  gap: 48px;
  max-width: 1320px;
  margin: 0 auto;
  padding: 32px;
}
.dev-sidebar {
  position: sticky;
  top: 80px;
  align-self: start;
  max-height: calc(100vh - 96px);
  overflow-y: auto;
  padding-bottom: 16px;
}
.dev-nav-link {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  border-radius: 8px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  color: rgba(var(--v-theme-on-surface), 0.72);
  transition: all 0.15s;
  text-decoration: none;
  margin-bottom: 2px;
}
.dev-nav-link:hover {
  background: rgba(var(--v-theme-on-surface), 0.06);
  color: rgb(var(--v-theme-on-surface));
}
.dev-nav-link.active {
  background: rgba(var(--v-theme-primary), 0.12);
  color: rgb(var(--v-theme-primary));
}
.method-chip {
  font-size: 0.62rem !important;
  font-weight: 700 !important;
  height: 18px !important;
  letter-spacing: 0.5px;
}
.dev-baseurl {
  margin-top: 24px;
  padding: 14px;
  border: 1px solid rgba(var(--v-theme-on-surface), 0.1);
  border-radius: 10px;
  background: rgba(var(--v-theme-on-surface), 0.02);
}
.dev-baseurl-code {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.dev-baseurl-code code {
  font-size: 0.78rem;
  font-family: ui-monospace, "SF Mono", Menlo, monospace;
  word-break: break-all;
}

.dev-content { min-width: 0; }
.doc-section {
  padding: 48px 0;
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.08);
}
.doc-section:first-child { padding-top: 16px; }
.doc-section:last-of-type { border-bottom: none; }

.doc-h1 {
  font-size: 2.5rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  margin-bottom: 16px;
  line-height: 1.15;
}
.doc-h2 {
  font-size: 1.75rem;
  font-weight: 700;
  letter-spacing: -0.015em;
  margin-bottom: 12px;
  line-height: 1.25;
}
.doc-h3 {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: rgba(var(--v-theme-on-surface), 0.6);
  margin: 24px 0 12px;
}
.doc-lead {
  font-size: 1.125rem;
  line-height: 1.65;
  color: rgba(var(--v-theme-on-surface), 0.85);
  max-width: 720px;
}
.doc-p {
  font-size: 0.95rem;
  line-height: 1.7;
  color: rgba(var(--v-theme-on-surface), 0.82);
  margin-bottom: 12px;
}
.doc-p code {
  background: rgba(var(--v-theme-on-surface), 0.07);
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 0.88em;
}
.doc-note {
  font-size: 0.85rem;
  font-style: italic;
  color: rgba(var(--v-theme-on-surface), 0.6);
  border-left: 3px solid rgba(var(--v-theme-primary), 0.5);
  padding: 4px 12px;
  margin-top: 12px;
}

/* Endpoint */
.endpoint-section { padding-top: 56px; }
.endpoint-head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 4px;
}
.endpoint-path {
  font-size: 1rem;
  font-weight: 600;
  font-family: ui-monospace, "SF Mono", Menlo, monospace;
  color: rgb(var(--v-theme-on-surface));
  background: rgba(var(--v-theme-on-surface), 0.06);
  padding: 4px 10px;
  border-radius: 6px;
}
.method-chip-lg {
  font-weight: 700 !important;
  letter-spacing: 0.5px;
  height: 24px !important;
}
.endpoint-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 32px;
  margin-top: 16px;
}
.endpoint-examples { min-width: 0; }

/* Param table */
.param-table {
  border: 1px solid rgba(var(--v-theme-on-surface), 0.1);
  border-radius: 10px;
  overflow: hidden;
}
.param-row {
  display: grid;
  grid-template-columns: 240px 1fr;
  padding: 14px 16px;
  gap: 16px;
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.07);
  font-size: 0.875rem;
}
.param-row:last-child { border-bottom: none; }
.param-name code {
  font-weight: 600;
  font-size: 0.88rem;
  color: rgb(var(--v-theme-on-surface));
  background: rgba(var(--v-theme-on-surface), 0.06);
  padding: 2px 8px;
  border-radius: 4px;
}
.param-meta {
  display: block;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: rgba(var(--v-theme-on-surface), 0.5);
  margin-top: 2px;
}
.param-desc {
  color: rgba(var(--v-theme-on-surface), 0.82);
  line-height: 1.5;
}

/* Code blocks — dark always */
.code-card {
  background: #0d1117;
  border-radius: 10px;
  overflow: hidden;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.18);
  border: 1px solid rgba(255, 255, 255, 0.06);
}
.code-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 14px;
  background: #161b22;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}
.code-lang {
  font-size: 0.72rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.55);
}
.code-body {
  margin: 0;
  padding: 18px 20px;
  overflow-x: auto;
  font-family: ui-monospace, "SF Mono", "JetBrains Mono", Menlo, Monaco, "Cascadia Code", monospace;
  font-size: 0.83rem;
  line-height: 1.6;
  color: #c9d1d9;
  white-space: pre;
  tab-size: 2;
}
.code-body code {
  font-family: inherit;
  background: transparent;
  color: inherit;
}

.lang-tab {
  background: transparent;
  border: none;
  color: rgba(255, 255, 255, 0.55);
  font-size: 0.78rem;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 6px;
  cursor: pointer;
  letter-spacing: 0.02em;
  transition: all 0.15s;
}
.lang-tab:hover { color: rgba(255, 255, 255, 0.85); }
.lang-tab.active {
  background: rgba(255, 255, 255, 0.1);
  color: #ffffff;
}

.postman-pre {
  max-height: 480px;
  overflow: auto;
}

.dev-footer {
  padding: 48px 0 32px;
  text-align: center;
  font-size: 0.82rem;
  color: rgba(var(--v-theme-on-surface), 0.4);
}

@media (max-width: 1100px) {
  .endpoint-grid { grid-template-columns: 1fr; }
}
@media (max-width: 900px) {
  .dev-body { grid-template-columns: 1fr; gap: 16px; padding: 20px; }
  .dev-sidebar { position: relative; top: 0; max-height: none; }
  .doc-h1 { font-size: 2rem; }
  .doc-h2 { font-size: 1.4rem; }
  .param-row { grid-template-columns: 1fr; gap: 4px; }
}

/* Durum kartları (transaction states + callback outcomes) */
.status-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 12px;
  margin: 12px 0 20px;
}
.status-card {
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-left-width: 4px;
  border-radius: 8px;
  padding: 12px 14px;
  background: rgb(var(--v-theme-surface));
}
.status-card-head {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 6px;
}
.status-state-tag {
  font-family: var(--v-font-family-mono, ui-monospace, monospace);
  font-size: 0.78rem;
  color: rgba(var(--v-theme-on-surface), 0.6);
}
.status-card-body {
  font-size: 0.86rem;
  line-height: 1.5;
  margin: 0;
  color: rgba(var(--v-theme-on-surface), 0.84);
}
.status-card.status-pending    { border-left-color: rgb(var(--v-theme-warning)); background: rgba(var(--v-theme-warning), 0.05); }
.status-card.status-processing { border-left-color: rgb(var(--v-theme-info));    background: rgba(var(--v-theme-info), 0.05); }
.status-card.status-approved   { border-left-color: rgb(var(--v-theme-success)); background: rgba(var(--v-theme-success), 0.05); }
.status-card.status-rejected   { border-left-color: rgb(var(--v-theme-error));   background: rgba(var(--v-theme-error), 0.05); }
</style>
