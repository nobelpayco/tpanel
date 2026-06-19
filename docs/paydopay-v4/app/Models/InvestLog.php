<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;

class InvestLog extends Model
{
    protected $table = 'investLog';
    public $timestamps = false;

    protected $fillable = ['investID', 'userID', 'ip', 'status', 'createdAt', 'detail'];

    protected function casts(): array
    {
        return ['createdAt' => 'datetime'];
    }

    public function invest(): BelongsTo
    {
        return $this->belongsTo(Invest::class, 'investID');
    }

    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class, 'userID');
    }
}
