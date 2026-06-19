<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Merchant extends Model
{
    protected $table = 'merchantUser';
    public $timestamps = false;
    const CREATED_AT = 'created_At';

    protected $fillable = [
        'status', 'name', 'email', 'password', 'apiKey', 'depositLimit',
        'minDeposit', 'maxDeposit', 'commission', 'withdrawCommission',
        'caseNow', 'useWallet', 'approved_ip',
    ];

    protected $hidden = ['password', 'apiKey'];

    public function investments(): HasMany
    {
        return $this->hasMany(Invest::class, 'firm_id');
    }
}
