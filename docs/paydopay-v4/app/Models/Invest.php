<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Invest extends Model
{
    protected $table = 'invest';
    public $timestamps = false;
    const CREATED_AT = 'created_at';

    // Status
    const STATUS_CANCEL     = 0;
    const STATUS_PENDING    = 1;
    const STATUS_PROCESSING = 2;
    const STATUS_APPROVED   = 3;
    const STATUS_REJECTED   = 4;

    // Type
    const TYPE_DEPOSIT  = 1;
    const TYPE_WITHDRAW = 2;

    /**
     * Mass assignment koruması: $fillable yerine $guarded kullanılır.
     * Controller'lar kullanıcı input'unu doğrudan `fill($request->all())` ile bu modele aktarmasın;
     * sadece açık DB::table()->update($explicitFields) veya $request->only([...]) ile alanlar belirtilir.
     * Bu, geleceğin endpoint'lerinde yanlışlıkla `status`, `amount`, `firm_id`, `agent_id` gibi kritik
     * alanların kullanıcı tarafından override edilmesini engeller.
     */
    protected $guarded = [
        'id',
        'status', 'firm_id', 'team_id', 'agent_id',
        'panel_commissin_amount', 'team_commissin_amount',
        'panel_commission_percent', 'team_commission_percent',
        'amountChanged', 'callbackSended', 'isControled', 'isConverted',
        'finalize_date', 'process_date', 'rejectType',
        'created_at', 'updated_at',
    ];

    protected function casts(): array
    {
        return [
            'created_at'    => 'datetime',
            'form_at'       => 'datetime',
            'process_date'  => 'datetime',
            'finalize_date' => 'datetime',
            'amount'        => 'decimal:2',
            'payed_amount'  => 'decimal:2',
        ];
    }

    public function merchant(): BelongsTo
    {
        return $this->belongsTo(Merchant::class, 'firm_id');
    }

    public function team(): BelongsTo
    {
        return $this->belongsTo(Team::class, 'team_id');
    }

    public function agent(): BelongsTo
    {
        return $this->belongsTo(User::class, 'agent_id');
    }

    public function bankAccount(): BelongsTo
    {
        return $this->belongsTo(BankAccount::class, 'bank_id');
    }

    public function logs(): HasMany
    {
        return $this->hasMany(InvestLog::class, 'investID');
    }

    // Scopes
    public function scopeDeposits($query) { return $query->where('type', self::TYPE_DEPOSIT); }
    public function scopeWithdrawals($query) { return $query->where('type', self::TYPE_WITHDRAW); }
    public function scopePending($query) { return $query->where('status', self::STATUS_PENDING); }
    public function scopeApproved($query) { return $query->where('status', self::STATUS_APPROVED); }
    public function scopeRejected($query) { return $query->where('status', self::STATUS_REJECTED); }

    public function isDeposit(): bool { return $this->type == self::TYPE_DEPOSIT; }
    public function isWithdraw(): bool { return $this->type == self::TYPE_WITHDRAW; }
}
