<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class WalletProvider extends Model
{
    protected $table = 'walletProviders';
    public $timestamps = false;

    protected $fillable = ['name', 'status'];
}
