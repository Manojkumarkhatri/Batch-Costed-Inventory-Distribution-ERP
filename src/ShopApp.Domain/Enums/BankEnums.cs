namespace ShopApp.Domain.Enums;

/// <summary>
/// What kind of movement a bank row is. Cash appears here as a counterparty
/// rather than as an account of its own - the app tracks what is in the bank,
/// and cash in hand is the cash flow report's job.
/// </summary>
public enum BankTxnType
{
    Deposit,          // cash paid in at the counter
    Withdraw,         // cash taken out
    BankToCash,       // moved out of the bank into the till
    CashToBank,       // moved out of the till into the bank
    BankToBank,       // one half of a transfer between two accounts
    Adjustment,       // correcting the book balance to match the statement
    Opening           // reserved: the opening figure lives on the account
}
