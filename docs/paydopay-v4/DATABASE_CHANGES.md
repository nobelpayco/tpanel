# Veritabanı Değişiklikleri

Bu dosya, yedek veritabanında (havalev3_new) yapılan değişiklikleri takip eder.
Live veritabanına geçişte bu değişikliklerin uygulanması gerekir.

---

## Yeni Tablolar

### bankAccounts — daily_count_limit + sort_order kolonları eklendi
```sql
ALTER TABLE `bankAccounts` ADD COLUMN `daily_count_limit` INT NOT NULL DEFAULT 0 AFTER `max_per_invest`;
ALTER TABLE `bankAccounts` ADD COLUMN `sort_order` INT NOT NULL DEFAULT 0;
```
**Amaç:**
- `daily_count_limit`: Günlük işlem adet limiti. 0 = sınırsız. Bugün bu hesaba düşmüş status∈{1,2,3} olan invest sayısı limit'i aşmışsa API'de listede çıkmaz.
- `sort_order`: IBAN önerme sırası. Küçük değer önce. Eşitlik durumunda random. Panel'den sürükle-bırak veya numara girerek değiştirilir.

### invest — receipt_path kolonu eklendi
```sql
ALTER TABLE `invest` ADD COLUMN `receipt_path` VARCHAR(500) NULL AFTER `iban`;
```
**Amaç:** Oyuncunun ödeme sayfasında yüklediği dekont/makbuz dosyasının `storage/app/public/receipts/` altındaki yol. Daha hızlı admin onayı için ipucu sağlar.

### merchantUser — apiSecret kolonu eklendi
```sql
ALTER TABLE `merchantUser`
  ADD COLUMN `apiSecret` VARCHAR(64) NULL AFTER `apiKey`,
  ADD INDEX `merchantUser_apiKey_idx` (`apiKey`(64));
```
**Amaç:** Merchant API v1 (HMAC tabanlı auth) için imza secret'ı. `apiKey` identifier olarak header'da gönderilir, `apiSecret` body imzalama için kullanılır, asla request'te gönderilmez. Mevcut merchant'lar için `artisan apikeys:provision-secrets` ile doldurulur.

**Not:** `apiKey` kolonu canlıda mediumtext olarak tanımlı; TEXT tipleri üzerine direkt index alınamadığı için prefix uzunluğu (64) belirtilmiştir. Mevcut değerler 30-32 karakter olduğundan tam değer index kapsamında kalır.

### sessions (Laravel — database session driver)
```sql
CREATE TABLE `sessions` (
    `id` varchar(255) NOT NULL,
    `user_id` bigint unsigned NULL,
    `ip_address` varchar(45) NULL,
    `user_agent` text NULL,
    `payload` longtext NOT NULL,
    `last_activity` int NOT NULL,
    PRIMARY KEY (`id`),
    KEY `sessions_user_id_index` (`user_id`),
    KEY `sessions_last_activity_index` (`last_activity`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```
**Amaç:** `SESSION_DRIVER=database` kullanıldığında zorunlu. Laravel default migrations'ı içerir ama mevcut DB'lere migrate çalıştırılmadığı için manuel oluşturulmalı.

### personal_access_tokens (Laravel Sanctum)
```sql
CREATE TABLE `personal_access_tokens` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `tokenable_type` VARCHAR(255) NOT NULL,
    `tokenable_id` BIGINT UNSIGNED NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `token` VARCHAR(64) NOT NULL,
    `abilities` TEXT NULL,
    `last_used_at` TIMESTAMP NULL,
    `expires_at` TIMESTAMP NULL,
    `created_at` TIMESTAMP NULL,
    `updated_at` TIMESTAMP NULL,
    UNIQUE INDEX `personal_access_tokens_token_unique` (`token`),
    INDEX `personal_access_tokens_tokenable_type_tokenable_id_index` (`tokenable_type`, `tokenable_id`)
);
```

---

## Tablo Değişiklikleri

### users — created_by kolonu eklendi
```sql
ALTER TABLE `users` ADD COLUMN `created_by` INT(11) NULL AFTER `auto_mode_change`;
```
**Amaç:** Team Admin hiyerarşisi — ana team admin (created_by=NULL) ve alt team adminler (created_by=team_admin_id) ayrımı.

### users — user_type yeni rol değerleri
```
1 = Super Admin (mevcut)
2 = Team Agent (mevcut)
3 = Merchant (mevcut)
4 = Sub Admin (yeni)
5 = Team Admin (yeni)
6 = Blocked (yeni)
```

### new_intermediaries (Aracılar)
```sql
CREATE TABLE `new_intermediaries` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `type` TINYINT NOT NULL, -- 1=paylira_aracisi, 2=merchant_aracisi
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);
```

### new_intermediary_merchant (Aracı-Merchant İlişkisi)
```sql
CREATE TABLE `new_intermediary_merchant` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `merchant_id` INT UNSIGNED NOT NULL,
    `commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);
```

### new_intermediary_team (Aracı-Takım İlişkisi)
```sql
CREATE TABLE `new_intermediary_team` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `team_id` INT UNSIGNED NOT NULL,
    `commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);
```

### fund_storages (Fon Depoları)
```sql
CREATE TABLE `fund_storages` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `type` TINYINT NOT NULL, -- 1=dövizci(dış kaynak), 2=soğuk cüzdan(iç kaynak)
    `wallet_address` VARCHAR(255) NULL,
    `balance` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL,
    `updated_at` TIMESTAMP NULL
);
```

### merchant_groups (Merchant Grupları)
```sql
CREATE TABLE `merchant_groups` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);
```

### merchantUser — group_id kolonu eklendi
```sql
ALTER TABLE `merchantUser` ADD COLUMN `group_id` INT UNSIGNED NULL AFTER `id`;
```
**Amaç:** Aynı müşteriye ait merchant'ları gruplama (örn: PINCO Super + PINCO H2H = PINCO grubu)

### users — merchant_group_id kolonu eklendi
```sql
ALTER TABLE `users` ADD COLUMN `merchant_group_id` INT UNSIGNED NULL AFTER `firm_id`;
```
**Amaç:** Merchant kullanıcısı gruba bağlandığında tüm grup merchant'larının verilerini görebilir

### merchant_payments — delivery komisyon alanları eklendi
```sql
ALTER TABLE `merchant_payments` ADD COLUMN `delivery_commission_rate` DECIMAL(5,2) NOT NULL DEFAULT 0 AFTER `amount`;
ALTER TABLE `merchant_payments` ADD COLUMN `delivery_commission_amount` DECIMAL(15,2) NOT NULL DEFAULT 0 AFTER `delivery_commission_rate`;
```

### merchantUser — deliveryCommission kolonu eklendi
```sql
ALTER TABLE `merchantUser` ADD COLUMN `deliveryCommission` DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER `withdrawCommission`;
```
**Amaç:** Merchant'a ödeme yapılırken alınan komisyon oranı. Paylira net kazancına eklenir.

### merchant_payments (Merchant Ödemeleri)
```sql
CREATE TABLE `merchant_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `merchant_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL, -- 1=TL, 2=Kripto
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);
```

### new_intermediaries — balance kolonu eklendi
```sql
ALTER TABLE `new_intermediaries` ADD COLUMN `balance` DECIMAL(15,2) NOT NULL DEFAULT 0 AFTER `status`;
```
**Amaç:** Aracı komisyon bakiyesi birikimli tutulur, gün sonu snapshot ile güncellenir.

### daily_case_snapshots (Günlük Kasa Snapshot)
```sql
CREATE TABLE `daily_case_snapshots` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `snapshot_date` DATE NOT NULL,
    `entity_type` VARCHAR(20) NOT NULL, -- merchant, intermediary, paylira, team
    `entity_id` INT UNSIGNED NULL,
    `entity_name` VARCHAR(255) NOT NULL,
    `amount` DECIMAL(15,2) NOT NULL DEFAULT 0,
    `details` JSON NULL,
    `created_at` TIMESTAMP NULL,
    UNIQUE INDEX `daily_snapshot_unique` (`snapshot_date`, `entity_type`, `entity_id`),
    INDEX `idx_snapshot_date` (`snapshot_date`)
);
```
**Cron:** Her gün 23:59'da `php artisan snapshot:daily` komutu çalışır.

### paylira_partners (Ortaklar)
```sql
CREATE TABLE `paylira_partners` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `share_percent` DECIMAL(5,2) NOT NULL,
    `status` TINYINT NOT NULL DEFAULT 1,
    `created_at` TIMESTAMP NULL
);

INSERT INTO `paylira_partners` (`name`, `share_percent`, `status`, `created_at`) VALUES
('Heav', 50.00, 1, NOW()),
('Bien', 50.00, 1, NOW());
```

### paylira_partner_payments (Ortak Ödemeleri)
```sql
CREATE TABLE `paylira_partner_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `partner_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL, -- 1=TL, 2=Kripto
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);
```

### team_payments (Takım Ödemeleri)
```sql
CREATE TABLE `team_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `team_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL, -- 1=TL, 2=Kripto
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);
```

### intermediary_payments (Aracı Ödemeleri)
```sql
CREATE TABLE `intermediary_payments` (
    `id` INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `intermediary_id` INT UNSIGNED NOT NULL,
    `payment_type` TINYINT NOT NULL, -- 1=TL, 2=Kripto, 3=Grup Alacak Mahsup
    `amount` DECIMAL(15,2) NOT NULL,
    `crypto_quantity` DECIMAL(15,6) NULL,
    `crypto_rate` DECIMAL(15,2) NULL,
    `tx_link` VARCHAR(500) NULL,
    `team_id` INT UNSIGNED NULL,
    `description` TEXT NULL,
    `created_by` INT UNSIGNED NULL,
    `created_at` TIMESTAMP NULL
);
```

### `invest.original_amount` (YENİ)
```sql
ALTER TABLE invest ADD COLUMN original_amount DECIMAL(15,2) NULL AFTER amount;
```
**Amaç:** Merchant API'den gelen orijinal yatırım tutarını sakla. Oyuncu pay sayfasında farklı bir tutar deklare ederse `invest.amount` güncellenir; `original_amount` ise API'den geldiği şekilde kalır. Panelde admin ikisini yan yana karşılaştırabilir.

---

### `teams.allow_duplicate_iban` (YENİ)
```sql
ALTER TABLE teams ADD COLUMN allow_duplicate_iban TINYINT(1) NOT NULL DEFAULT 0 AFTER maxCase;
UPDATE teams SET allow_duplicate_iban = 0;
```
**Amaç:** Bir takıma yeni banka hesabı eklenirken aynı IBAN'ın sistemde başka yerde var olup olmadığı kontrolü. Varsayılan `0` (eklenemez) — aynı IBAN sisteme yalnızca bir kez girilebilir. `1` yapılırsa o takım için duplicate IBAN'a izin verilir.

### invest_receipts (Çekim Dekontları — çoklu)
```sql
CREATE TABLE invest_receipts (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    invest_id INT UNSIGNED NOT NULL,
    file_path VARCHAR(500) NOT NULL,
    original_name VARCHAR(255) NULL,
    mime_type VARCHAR(100) NULL,
    file_size INT UNSIGNED NULL,
    uploaded_by INT UNSIGNED NULL,
    uploaded_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_invest_id (invest_id),
    INDEX idx_uploaded_by (uploaded_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```
**Amaç:** Çekim işlemlerine yüklenen dekontlar. Çoklu — bir çekime birden fazla dekont eklenebilir, eski silinmez. Dosya `storage/app/public/receipts/withdrawals/{invest_id}/{uuid}.{ext}` altında durur. Onay (`approve()`) için en az 1 dekont zorunludur. Yetki: user_type IN (1, 2, 4, 5) — Super Admin, Team Agent, Sub Admin, Team Admin. Kabul edilen formatlar: PDF, JPG, PNG, WEBP — max 10 MB.

### teams — Telegram bildirim alanları (3 chat ID + 3 switch + threshold + state)
```sql
ALTER TABLE teams
  ADD COLUMN telegram_reconciliation_chat_id VARCHAR(50) NULL AFTER telegram_withdraw_chat_id,
  ADD COLUMN telegram_credit_low_enabled TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN telegram_credit_low_threshold DECIMAL(15,2) NULL,
  ADD COLUMN telegram_credit_low_state TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN telegram_pending_invest_enabled TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN telegram_missing_receipt_enabled TINYINT(1) NOT NULL DEFAULT 0;

UPDATE teams SET telegram_pending_invest_enabled = 1 WHERE telegram_enabled = 1;
```
**Amaç:** Takım bazlı Telegram bildirim yapılandırması.
- `telegram_chat_id` = Destek Chat (mevcut, üstlenilmedi/sonuçlandırılmadı/kredi azaldı buraya).
- `telegram_withdraw_chat_id` = Çekim Chat (mevcut, dekont yüklenmedi buraya).
- `telegram_reconciliation_chat_id` = Mutabakat Chat (yeni, ileride mutabakat bildirimleri için).
- `telegram_credit_low_enabled` + `telegram_credit_low_threshold` = Kredi Azaldı switch + eşik tutarı. Anlık kasa ≥ (maxCase - threshold) olduğunda destek chat'e mesaj. Edge-trigger: bir kez gönderilir, kasa eşik üstüne çıkıp yine alt geçince yeni mesaj.
- `telegram_credit_low_state` = Edge-trigger state. 0=normal, 1=düşük (mesaj atılmış).
- `telegram_pending_invest_enabled` = İşlem Sonuçlandırılmadı switch. 10dk 30sn'dir status=2'de olan yatırımlar için destek chat'e.
- `telegram_missing_receipt_enabled` = Dekont Yüklenmedi switch. Onaylanmış (status=3) ama 10dk'dır dekont yüklenmemiş çekimler için çekim chat'e.

Geriye uyumluluk: mevcut `telegram_enabled=1` olan takımlara `telegram_pending_invest_enabled=1` backfill edildi.

### teams — Kasa Raporu webhook switch
```sql
ALTER TABLE teams ADD COLUMN telegram_cash_report_enabled TINYINT(1) NOT NULL DEFAULT 0;
```
**Amaç:** Aktif edilen takım için, Mutabakat Chat ID grubuna `@paylira_reminder_bot kasa` yazıldığında bot anlık kasa raporu cevap verir. Webhook üzerinden incoming message dinlenir; `telegram_reconciliation_chat_id` eşleştirmesiyle takım bulunur. Stateless — `enabled_at` gerekmez (canlı sorgu yanıtı).

### teams — Telegram switch enabled_at timestamps
```sql
ALTER TABLE teams
  ADD COLUMN telegram_credit_low_enabled_at TIMESTAMP NULL,
  ADD COLUMN telegram_pending_invest_enabled_at TIMESTAMP NULL,
  ADD COLUMN telegram_missing_receipt_enabled_at TIMESTAMP NULL;
```
**Amaç:** Her switch ne zaman açıldığını sakla. Cron sadece bu timestamp'ten sonra **oluşan** invest'ler için bildirim gönderir — switch açılmadan önceki geçmiş işlemlere bildirim gitmez. TeamController switch'i 0→1 yaparken otomatik `now()` set eder. 1→0'da değer kalır (yeniden açılırsa üzerine yazılır).

### teams — block_when_full kolonu eklendi
```sql
ALTER TABLE teams ADD COLUMN block_when_full TINYINT(1) NOT NULL DEFAULT 1 AFTER allow_duplicate_iban;
```
**Amaç:** Takımın anlık kasası `maxCase`'e ulaşmışsa yeni yatırım yönlendirmesini engelle. `1` (varsayılan): tam dolduğunda takım listeden düşer. `0`: dolu olsa bile devam eder. `MerchantBankService::teamsAtFullCase()` ve `TeamController` store/update tarafından kullanılır. Bu kolon önceki migration paketinde unutulmuştu; kodun kullanması nedeniyle takım düzenleme ve dashboard stats endpoint'leri SQLSTATE[42S22] hatası veriyordu.

---

## Notlar
- Yedek DB: havalev3_new @ 51.178.199.190
- Live DB: havalev3_havale2 @ 51.178.199.190
