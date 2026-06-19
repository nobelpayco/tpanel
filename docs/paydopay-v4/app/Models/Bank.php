<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\HasMany;

class Bank extends Model
{
    protected $table = 'banks';
    public $timestamps = false;

    protected $fillable = ['name', 'code', 'logo'];

    public function accounts(): HasMany
    {
        return $this->hasMany(BankAccount::class, 'bank_id');
    }
}
