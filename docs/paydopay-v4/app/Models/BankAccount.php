<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class BankAccount extends Model
{
    protected $table = 'bankAccounts';
    public $timestamps = false;
    const CREATED_AT = 'created_at';

    protected $fillable = [
        'bank_id', 'account_holder', 'account_iban', 'account_code',
        'min_invest', 'max_invest', 'max_per_invest', 'max_amount',
        'dailyInvest', 'status', 'team_id', 'walletID', 'isFast',
    ];

    public function bank(): BelongsTo
    {
        return $this->belongsTo(Bank::class, 'bank_id');
    }

    public function team(): BelongsTo
    {
        return $this->belongsTo(Team::class, 'team_id');
    }
}
