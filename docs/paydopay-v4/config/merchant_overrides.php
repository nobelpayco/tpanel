<?php

/**
 * Merchant API'sine özel kurallar.
 *
 * Eski sistemde hardcoded `if ($merchantId == 30) { team=106 }` gibi durumlar vardı.
 * Burada merchant_id => kural map'i.
 *
 * Örnek:
 *   30 => [ 'forced_team_id' => 106 ],
 *
 * Forced team yoksa MerchantBankService default (tüm uygun takımlar) davranır.
 */

return [
    // 30 => ['forced_team_id' => 106],
];
