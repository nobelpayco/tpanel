<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\User;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class UserController extends Controller
{
    private function gate(Request $request): User
    {
        $user = $request->user();
        if (! $user->canManageUsers()) {
            abort(403, 'Bu sayfaya erişim yetkiniz yok.');
        }
        return $user;
    }

    public function index(Request $request): JsonResponse
    {
        $authUser = $this->gate($request);

        $query = DB::table('users')
            ->leftJoin('teams', 'users.team_id', '=', 'teams.id')
            ->leftJoin('merchantUser', 'users.firm_id', '=', 'merchantUser.id')
            ->select(
                'users.id', 'users.name', 'users.username', 'users.user_type',
                'users.status', 'users.team_id', 'users.firm_id',
                'users.created_at', 'users.created_by', 'users.last_login',
                'teams.name as team_name',
                'merchantUser.name as merchant_name',
            );

        // Team Admin sadece kendi takımının kullanıcılarını (+ kendisini) görür
        if ($authUser->isTeamAdmin()) {
            $query->where(function ($q) use ($authUser) {
                $q->where('users.team_id', $authUser->team_id)
                  ->orWhere('users.id', $authUser->id);
            });
        }

        if ($request->filled('user_type') && $request->user_type !== 'all') {
            $query->where('users.user_type', (int) $request->user_type);
        }
        if ($request->filled('status') && $request->status !== 'all') {
            $query->where('users.status', $request->status);
        }
        if ($request->filled('search')) {
            $s = $request->search;
            $query->where(function ($q) use ($s) {
                $q->where('users.name', 'like', "%{$s}%")
                  ->orWhere('users.username', 'like', "%{$s}%");
            });
        }

        $rows = $query->orderByDesc('users.id')->limit(500)->get();

        $roleLabels = User::ROLE_LABELS;
        $items = $rows->map(function ($u) use ($roleLabels) {
            $u->role_label = $roleLabels[$u->user_type] ?? '-';
            return $u;
        });

        return response()->json($items);
    }

    public function options(Request $request): JsonResponse
    {
        $authUser = $this->gate($request);

        $allowed = $authUser->creatableUserTypes();
        $roleLabels = User::ROLE_LABELS;
        $allowedRoles = collect($allowed)
            ->map(fn ($t) => ['id' => $t, 'label' => $roleLabels[$t] ?? '-'])
            ->values();

        // Team Admin için takım listesi gereksiz (kendi takımı otomatik)
        $teams = $authUser->isTeamAdmin()
            ? []
            : DB::table('teams')->select('id', 'name', 'status')->orderBy('name')->get();

        $merchants = DB::table('merchantUser')
            ->select('id', 'name', 'status')
            ->orderBy('name')
            ->get();

        return response()->json([
            'allowed_roles' => $allowedRoles,
            'teams'         => $teams,
            'merchants'     => $merchants,
            'role_labels'   => $roleLabels,
        ]);
    }

    public function store(Request $request): JsonResponse
    {
        $authUser = $this->gate($request);

        $request->validate([
            'name'      => 'required|string|max:255',
            'username'  => 'required|string|max:255|unique:users,username',
            'password'  => ['required', 'string', 'min:6', 'regex:/^(?=.*[A-Za-z])(?=.*\d).+$/'],
            'user_type' => 'required|integer',
            'team_id'   => 'nullable|integer',
            'firm_id'   => 'nullable|integer',
            'status'    => 'required|in:0,1',
        ], [
            'password.min'    => 'Şifre en az 6 karakter olmalıdır.',
            'password.regex'  => 'Şifre en az bir harf ve bir rakam içermelidir.',
        ]);

        $targetType = (int) $request->user_type;
        if (! $authUser->canCreateUserType($targetType)) {
            abort(403, 'Bu kullanıcı tipini yaratma yetkiniz yok.');
        }

        $teamId = 0;
        $firmId = null;

        if ($targetType === User::ROLE_TEAM_AGENT) {
            // Team Admin → kendi takımı otomatik
            if ($authUser->isTeamAdmin()) {
                $teamId = (int) $authUser->team_id;
            } else {
                if (! $request->filled('team_id')) {
                    return response()->json(['message' => 'Takım seçimi zorunludur.'], 422);
                }
                $teamId = (int) $request->team_id;
                if (! DB::table('teams')->where('id', $teamId)->exists()) {
                    return response()->json(['message' => 'Takım bulunamadı.'], 422);
                }
            }
        } elseif ($targetType === User::ROLE_TEAM_ADMIN) {
            if (! $request->filled('team_id')) {
                return response()->json(['message' => 'Takım seçimi zorunludur.'], 422);
            }
            $teamId = (int) $request->team_id;
            if (! DB::table('teams')->where('id', $teamId)->exists()) {
                return response()->json(['message' => 'Takım bulunamadı.'], 422);
            }
        } elseif ($targetType === User::ROLE_MERCHANT) {
            if (! $request->filled('firm_id')) {
                return response()->json(['message' => 'Merchant seçimi zorunludur.'], 422);
            }
            $firmId = (int) $request->firm_id;
            if (! DB::table('merchantUser')->where('id', $firmId)->exists()) {
                return response()->json(['message' => 'Merchant bulunamadı.'], 422);
            }
        }

        $id = DB::table('users')->insertGetId([
            'name'              => $request->name,
            'username'          => $request->username,
            'password'          => md5($request->password),
            'user_type'         => $targetType,
            'team_id'           => $teamId,
            'firm_id'           => $firmId,
            'merchant_group_id' => null,
            'status'            => (string) $request->status,
            // Yeni kullanıcılarda 2FA varsayılan olarak KAPALI — admin sonradan profilden etkinleştirebilir
            'otp_ok'            => '0',
            'otp_code'          => '',
            'created_by'        => $authUser->id,
            'created_at'        => now(),
        ]);

        return response()->json(['message' => 'Kullanıcı eklendi.', 'id' => $id]);
    }

    public function update(int $id, Request $request): JsonResponse
    {
        $authUser = $this->gate($request);
        $target = User::find($id);
        if (! $target) {
            return response()->json(['message' => 'Kullanıcı bulunamadı.'], 404);
        }
        if (! $authUser->canEditUser($target)) {
            abort(403, 'Bu kullanıcıyı düzenleme yetkiniz yok.');
        }
        // Team admin (user_type=5) kendi hesabını düzenleyemez
        if ($authUser->isTeamAdmin() && (int) $target->id === (int) $authUser->id) {
            abort(403, 'Kendi hesabınızı bu sayfadan düzenleyemezsiniz.');
        }

        $request->validate([
            'name'     => 'required|string|max:255',
            'username' => 'required|string|max:255|unique:users,username,' . $id,
            'password' => ['nullable', 'string', 'min:6', 'regex:/^(?=.*[A-Za-z])(?=.*\d).+$/'],
            'status'   => 'required|in:0,1',
            'team_id'  => 'nullable|integer',
            'firm_id'  => 'nullable|integer',
        ], [
            'password.min'   => 'Şifre en az 6 karakter olmalıdır.',
            'password.regex' => 'Şifre en az bir harf ve bir rakam içermelidir.',
        ]);

        $updates = [
            'name'     => $request->name,
            'username' => $request->username,
            'status'   => (string) $request->status,
        ];

        if ($request->filled('password')) {
            $updates['password'] = md5($request->password);
        }

        // user_type/team/firm değiştirme yalnızca super_admin / sub_admin için
        if ($authUser->isAdmin() && $request->filled('user_type')) {
            $newType = (int) $request->user_type;
            if (! $authUser->canCreateUserType($newType)) {
                abort(403, 'Bu kullanıcı tipine değiştirme yetkiniz yok.');
            }
            $updates['user_type'] = $newType;

            if (in_array($newType, [User::ROLE_TEAM_AGENT, User::ROLE_TEAM_ADMIN], true)) {
                $updates['team_id'] = (int) ($request->team_id ?? $target->team_id);
                $updates['firm_id'] = null;
            } elseif ($newType === User::ROLE_MERCHANT) {
                $updates['firm_id'] = (int) ($request->firm_id ?? $target->firm_id);
                $updates['team_id'] = 0;
            } else {
                $updates['team_id'] = 0;
                $updates['firm_id'] = null;
            }
        }

        DB::table('users')->where('id', $id)->update($updates);

        // Kullanıcı pasifleştirildiyse veya hesabı bloklayan bir tipe (Blocked) çevrildiyse
        // aktif oturumunu sonlandır (token sil → sonraki istekte 401)
        $statusBecamePassive = (string) $request->status === '0';
        $becameBlocked = isset($updates['user_type']) && $updates['user_type'] === User::ROLE_BLOCKED;
        if ($statusBecamePassive || $becameBlocked) {
            DB::table('personal_access_tokens')->where('tokenable_id', $id)->delete();
        }

        return response()->json(['message' => 'Kullanıcı güncellendi.']);
    }

    public function destroy(int $id, Request $request): JsonResponse
    {
        $authUser = $this->gate($request);
        // Team admin (user_type=5) hiç kullanıcı silemez
        if ($authUser->isTeamAdmin()) {
            abort(403, 'Kullanıcı silme yetkiniz yok.');
        }
        $target = User::find($id);
        if (! $target) {
            return response()->json(['message' => 'Kullanıcı bulunamadı.'], 404);
        }
        if (! $authUser->canEditUser($target)) {
            abort(403, 'Bu kullanıcıyı silme yetkiniz yok.');
        }
        if ((int) $target->id === (int) $authUser->id) {
            return response()->json(['message' => 'Kendinizi silemezsiniz.'], 422);
        }

        // Soft delete: status=0 + aktif oturumun (token) iptal edilmesi
        DB::table('users')->where('id', $id)->update(['status' => '0']);
        DB::table('personal_access_tokens')->where('tokenable_id', $id)->delete();

        return response()->json(['message' => 'Kullanıcı pasif edildi ve oturumu sonlandırıldı.']);
    }
}
