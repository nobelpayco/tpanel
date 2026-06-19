namespace PayDoPay.Domain.Enums;

/// <summary>Kullanıcı rolleri — users.user_type (Laravel User modeli sabitleri).</summary>
public enum UserType
{
    SuperAdmin = 1,
    TeamAgent = 2,
    Merchant = 3,
    SubAdmin = 4,
    TeamAdmin = 5,
    Blocked = 6,
}

/// <summary>invest.type — işlem türü.</summary>
public enum InvestType
{
    Deposit = 1,
    Withdraw = 2,
}

/// <summary>
/// invest.status — işlem yaşam döngüsü.
/// 0=Bekliyor/Havuz, 1=Pending (IBAN verildi), 2=Processing (ödendi/işleniyor), 3=Approved, 4=Rejected.
/// </summary>
public enum InvestStatus
{
    Waiting = 0,
    Pending = 1,
    Processing = 2,
    Approved = 3,
    Rejected = 4,
}
