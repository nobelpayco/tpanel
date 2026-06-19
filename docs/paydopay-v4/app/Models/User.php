<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Foundation\Auth\User as Authenticatable;
use Laravel\Sanctum\HasApiTokens;

class User extends Authenticatable
{
    use HasApiTokens;

    protected $table = 'users';
    public $timestamps = false;

    const ROLE_SUPER_ADMIN = 1;
    const ROLE_TEAM_AGENT  = 2;
    const ROLE_MERCHANT    = 3;
    const ROLE_SUB_ADMIN   = 4;
    const ROLE_TEAM_ADMIN  = 5;
    const ROLE_BLOCKED     = 6;

    const ROLE_LABELS = [
        self::ROLE_SUPER_ADMIN => 'Super Admin',
        self::ROLE_SUB_ADMIN   => 'Sub Admin',
        self::ROLE_TEAM_ADMIN  => 'Team Admin',
        self::ROLE_TEAM_AGENT  => 'Team Agent',
        self::ROLE_MERCHANT    => 'Merchant',
        self::ROLE_BLOCKED     => 'Blocked',
    ];

    protected $fillable = [
        'team_id', 'firm_id', 'user_type', 'name', 'username', 'password',
        'status', 'otp_ok', 'otp_code', 'last_login', 'collapse',
        'auto_reload', 'auto_mode_change', 'created_by', 'merchant_group_id',
    ];

    protected $hidden = [
        'password',
        'otp_code',
    ];

    protected function casts(): array
    {
        return [
            'last_login' => 'datetime',
        ];
    }

    // Eski DB md5 kullanıyor
    public function getAuthPassword(): string
    {
        return $this->password;
    }

    public function team(): BelongsTo
    {
        return $this->belongsTo(Team::class, 'team_id');
    }

    public function merchant(): BelongsTo
    {
        return $this->belongsTo(Merchant::class, 'firm_id');
    }

    /**
     * Merchant kullanıcısının bağlı olduğu grubun tüm merchant ID'leri
     */
    public function getMerchantIdsAttribute(): array
    {
        if (! $this->isMerchant()) return [];

        if ($this->merchant_group_id) {
            return \Illuminate\Support\Facades\DB::table('merchantUser')
                ->where('group_id', $this->merchant_group_id)
                ->pluck('id')
                ->toArray();
        }

        return $this->firm_id ? [$this->firm_id] : [];
    }

    public function getRoleLabelAttribute(): string
    {
        return self::ROLE_LABELS[$this->user_type] ?? 'Unknown';
    }

    // --- Rol kontrolleri ---

    public function isSuperAdmin(): bool { return $this->user_type == self::ROLE_SUPER_ADMIN; }
    public function isSubAdmin(): bool { return $this->user_type == self::ROLE_SUB_ADMIN; }
    public function isTeamAdmin(): bool { return $this->user_type == self::ROLE_TEAM_ADMIN; }
    public function isTeamAgent(): bool { return $this->user_type == self::ROLE_TEAM_AGENT; }
    public function isMerchant(): bool { return $this->user_type == self::ROLE_MERCHANT; }
    public function isBlocked(): bool { return $this->status == '0' || $this->user_type == self::ROLE_BLOCKED; }

    public function hasOtpEnabled(): bool { return $this->otp_ok == '1'; }

    // --- Yetki grupları ---

    public function isAdmin(): bool
    {
        return in_array($this->user_type, [self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN]);
    }

    public function isTeamMember(): bool
    {
        return in_array($this->user_type, [self::ROLE_TEAM_ADMIN, self::ROLE_TEAM_AGENT]);
    }

    public function hasGlobalAccess(): bool { return $this->isAdmin(); }
    public function hasTeamScope(): bool { return $this->isTeamMember(); }
    public function hasMerchantScope(): bool { return $this->isMerchant(); }

    // --- Yetki kontrolleri ---

    public function canManageUsers(): bool
    {
        return in_array($this->user_type, [self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN, self::ROLE_TEAM_ADMIN]);
    }

    /**
     * Bu kullanıcının yaratabileceği user_type listesi.
     * Super → Sub/Team Admin/Team Agent/Merchant
     * Sub   → Team Admin/Team Agent/Merchant
     * Team Admin → Team Agent
     */
    public function creatableUserTypes(): array
    {
        return match ((int) $this->user_type) {
            self::ROLE_SUPER_ADMIN => [self::ROLE_SUB_ADMIN, self::ROLE_TEAM_ADMIN, self::ROLE_TEAM_AGENT, self::ROLE_MERCHANT],
            self::ROLE_SUB_ADMIN   => [self::ROLE_TEAM_ADMIN, self::ROLE_TEAM_AGENT, self::ROLE_MERCHANT],
            self::ROLE_TEAM_ADMIN  => [self::ROLE_TEAM_AGENT],
            default => [],
        };
    }

    public function canCreateUserType(int $targetType): bool
    {
        return in_array($targetType, $this->creatableUserTypes(), true);
    }

    public function isPrimaryTeamAdmin(): bool
    {
        return $this->isTeamAdmin() && is_null($this->created_by);
    }

    public function canManageTeamAdmins(): bool
    {
        return $this->isSuperAdmin() || $this->isPrimaryTeamAdmin();
    }

    public function canEditUser(User $target): bool
    {
        // Süper admin hiçbir şekilde düzenlenemez / silinemez
        if ($target->isSuperAdmin()) return false;

        // Super admin her şeyi düzenleyebilir
        if ($this->isSuperAdmin()) return true;

        // Sub admin kendi kapsamında
        if ($this->isSubAdmin()) return true;

        // Team admin — aynı takımda olmalı
        if ($this->isTeamAdmin() && $target->team_id === $this->team_id) {
            // Alt team admin, diğer team adminleri düzenleyemez
            if ($target->isTeamAdmin() && !$this->isPrimaryTeamAdmin()) return false;
            // Ana team admin, herkesi düzenleyebilir
            return true;
        }

        return false;
    }

    public function canManageTeams(): bool { return $this->isAdmin(); }
    public function canToggleTeamStatus(): bool { return $this->isAdmin(); }
    public function canManageMerchants(): bool { return $this->isAdmin(); }

    public function canManageBankAccounts(): bool
    {
        return in_array($this->user_type, [self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN, self::ROLE_TEAM_ADMIN]);
    }

    public function canApproveTransactions(): bool
    {
        return in_array($this->user_type, [
            self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN,
            self::ROLE_TEAM_ADMIN, self::ROLE_TEAM_AGENT,
        ]);
    }

    public function canBlockUsers(): bool { return $this->isAdmin(); }
    public function canAccessSystemSettings(): bool { return $this->isSuperAdmin(); }

    public function canViewFinancialReports(): bool
    {
        return in_array($this->user_type, [self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN, self::ROLE_TEAM_ADMIN, self::ROLE_MERCHANT]);
    }

    public function canViewPerformanceReports(): bool
    {
        return in_array($this->user_type, [self::ROLE_SUPER_ADMIN, self::ROLE_SUB_ADMIN, self::ROLE_TEAM_ADMIN]);
    }
}
