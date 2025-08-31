using System.Threading.Tasks;

public interface IManuscriptWriter
{
    Task SaveAsync(BookSpecification spec, bool final = false);
}
