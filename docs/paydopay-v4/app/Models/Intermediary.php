<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsToMany;

class Intermediary extends Model
{
    protected $table = 'new_intermediaries';
    public $timestamps = false;
    const CREATED_AT = 'created_at';

    const TYPE_PAYLIRA  = 1;
    const TYPE_MERCHANT = 2;

    protected $fillable = ['name', 'type', 'status'];

    public function merchants(): BelongsToMany
    {
        return $this->belongsToMany(Merchant::class, 'new_intermediary_merchant', 'intermediary_id', 'merchant_id')
            ->withPivot('commission_rate', 'status');
    }

    public function isPayliraType(): bool { return $this->type == self::TYPE_PAYLIRA; }
    public function isMerchantType(): bool { return $this->type == self::TYPE_MERCHANT; }
}
