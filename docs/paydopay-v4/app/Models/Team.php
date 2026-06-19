<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Team extends Model
{
    protected $table = 'teams';
    public $timestamps = false;
    const CREATED_AT = 'created_at';

    protected $fillable = [
        'name', 'status', 'is_fast', 'daily_limit', 'min_invest', 'max_invest',
        'account_perm', 'note', 'allowed_customers', 'commission', 'wait_limit',
        'statusReason', 'overturn', 'withdraw', 'withdrawNow', 'withdrawNowCount',
        'eggsID', 'eggsIDAll', 'withdrawAll', 'winRate', 'teamNow', 'isWallet',
        'lastPayOut', 'provider', 'maxCase',
    ];

    public function users(): HasMany
    {
        return $this->hasMany(User::class, 'team_id');
    }

    public function bankAccounts(): HasMany
    {
        return $this->hasMany(BankAccount::class, 'team_id');
    }

    public function investments(): HasMany
    {
        return $this->hasMany(Invest::class, 'team_id');
    }
}
