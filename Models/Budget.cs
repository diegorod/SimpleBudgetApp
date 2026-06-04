using System.Text.Json.Serialization;

public class Budget
{
    public string Name { get; set; } = "My Budget";
    public string? GoogleClientId { get; set; }
    public List<MonthlyIncome> Incomes { get; set; } = new();
    public List<MonthlyExpense> Expenses { get; set; } = new();
    public List<BudgetNote> Notes { get; set; } = new();

    [JsonIgnore]
    public decimal MonthlyIncome => Incomes.Where(i => i.IsEnabled).Sum(i => i.Amount);

    [JsonIgnore]
    public decimal MonthlyExpense => Expenses.Where(e => e.IsEnabled).Sum(e => e.Amount);

    [JsonIgnore]
    public decimal MonthlyNet => MonthlyIncome - MonthlyExpense;

    [JsonIgnore]
    public decimal DailyNet => MonthlyNet / 30.44m;

    [JsonIgnore]
    public decimal DailyIncome => MonthlyIncome / 30.44m;

    [JsonIgnore]
    public decimal DailyExpense => MonthlyExpense / 30.44m;

    [JsonIgnore]
    public decimal YearlyExpense => MonthlyExpense * 12;

    [JsonIgnore]
    public decimal YearlyNet => MonthlyNet * 12;
}

public class MonthlyIncome
{
    public int Id { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public decimal YearlyAmount => Amount * 12;
}

public class MonthlyExpense
{
    public int Id { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public decimal YearlyAmount => Amount * 12;
}

public class BudgetNote
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Content { get; set; } = string.Empty;
}