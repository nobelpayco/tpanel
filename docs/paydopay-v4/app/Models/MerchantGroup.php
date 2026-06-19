<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Facades\DB;

class MerchantGroup extends Model
{
    protected $table = 'merchant_groups';
    public $timestamps = false;
    const CREATED_AT = 'created_at';

    protected $fillable = ['name', 'status'];

    public function merchants()
    {
        return DB::table('merchantUser')->where('group_id', $this->id)->get();
    }

    public function merchantIds(): array
    {
        return DB::table('merchantUser')->where('group_id', $this->id)->pluck('id')->toArray();
    }
}
