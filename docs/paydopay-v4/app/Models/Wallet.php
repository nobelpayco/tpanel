<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class Wallet extends Model
{
    protected $table = 'wallets';
    public $timestamps = false;

    protected $fillable = [
        'account_code', 'userCode', 'password', 'status', 'userId', 'token',
        'refreshToken', 'refreshTokenExpiryTime', 'wallet_userId', 'walletCode',
        'walletAccountName', 'walletAccountId', 'mobilePhoneNumber', 'accountName',
        'accountCode', 'currentBalance', 'availableBalance', 'cashBalance',
        'provider', 'dailyLimit', 'nowBalance', 'proxyData', 'created_user',
    ];

    protected $hidden = ['password', 'token', 'refreshToken', 'proxyData'];

    public function walletProvider(): BelongsTo
    {
        return $this->belongsTo(WalletProvider::class, 'provider');
    }
}
