using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;

namespace XafSearch.Module.Controllers;

/// <summary>
/// Adds a "Generate Demo Data" action that fills SampleCustomer and SampleOrder
/// with random test data.
/// </summary>
public class DemoDataController : ViewController
{
    private SimpleAction generateAction;

    private static readonly string[] FirstNames = { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Hank", "Iris", "Jack", "Karen", "Leo", "Mia", "Nick", "Olivia", "Paul", "Quinn", "Rita", "Sam", "Tina" };
    private static readonly string[] LastNames = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez", "Anderson", "Taylor", "Thomas", "Moore", "Jackson", "Martin", "Lee", "White", "Harris", "Clark" };
    private static readonly string[] Cities = { "Amsterdam", "Rotterdam", "Utrecht", "Den Haag", "Eindhoven", "Groningen", "Tilburg", "Almere", "Breda", "Nijmegen", "Haarlem", "Arnhem", "Zaanstad", "Amersfoort", "Apeldoorn" };
    private static readonly string[] Domains = { "example.com", "test.nl", "demo.org", "mail.com", "company.nl" };

    public DemoDataController()
    {
        TargetViewType = ViewType.Any;

        generateAction = new SimpleAction(this, "GenerateDemoData", PredefinedCategory.Tools)
        {
            Caption = "Generate Demo Data",
            ImageName = "Action_Debug_Start",
            ToolTip = "Create random SampleCustomer and SampleOrder records"
        };
        generateAction.Execute += GenerateAction_Execute;
    }

    private void GenerateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var random = new Random();
        var os = Application.CreateObjectSpace(typeof(SampleCustomer));

        // Check if data already exists
        var existingCustomers = os.GetObjectsQuery<SampleCustomer>().Count();
        if (existingCustomers > 0)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Demo data already exists ({existingCustomers} customers). Delete existing data first.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        // Create 20 customers
        var customers = new List<SampleCustomer>();
        for (int i = 0; i < 20; i++)
        {
            var customer = os.CreateObject<SampleCustomer>();
            var firstName = FirstNames[random.Next(FirstNames.Length)];
            var lastName = LastNames[random.Next(LastNames.Length)];
            customer.Name = $"{firstName} {lastName}";
            customer.Email = $"{firstName.ToLower()}.{lastName.ToLower()}@{Domains[random.Next(Domains.Length)]}";
            customer.City = Cities[random.Next(Cities.Length)];
            customer.Age = random.Next(20, 70);
            customer.CreatedDate = DateTime.Today.AddDays(-random.Next(1, 365));
            customer.IsActive = random.Next(100) < 80; // 80% active
            customers.Add(customer);
        }

        // Create 50 orders spread across customers
        for (int i = 0; i < 50; i++)
        {
            var order = os.CreateObject<SampleOrder>();
            order.OrderNumber = $"ORD-{DateTime.Now.Year}-{(i + 1):D4}";
            order.OrderDate = DateTime.Today.AddDays(-random.Next(0, 180));
            order.Quantity = random.Next(1, 25);
            order.TotalAmount = Math.Round((decimal)(random.NextDouble() * 1000 + 10), 2);
            order.Status = (OrderStatus)random.Next(0, 5);
            order.Customer = customers[random.Next(customers.Count)];
        }

        os.CommitChanges();

        Application.ShowViewStrategy.ShowMessage(
            "Created 20 customers and 50 orders.",
            InformationType.Success, 3000, InformationPosition.Top);
    }
}
