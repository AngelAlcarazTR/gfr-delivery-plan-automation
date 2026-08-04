namespace Core.Tests;

public class AdoSprintReaderTests
{
    [Fact]
    public void ParseCurrentSprint_RealAdoResponse_MapsToSprint()
    {
        // Arrange — el JSON REAL que devolvió ADO (sprint actual 2026_S16)
        var json = """
        {
          "count": 1,
          "value": [{
            "id": "2ba15a77-bdc6-47c0-a956-9188c9bc969a",
            "name": "2026_S16_Jul29-Aug11",
            "path": "TaxProf\\2026\\Q3\\2026_S16_Jul29-Aug11",
            "attributes": {
              "startDate": "2026-07-29T00:00:00Z",
              "finishDate": "2026-08-11T00:00:00Z",
              "timeFrame": "current"
            }
          }]
        }
        """;

        // Act — el parser del adaptador
        var sprint = AdoSprintReader.ParseCurrentSprint(json);

        // Assert — mapea correctamente a tu dominio
        Assert.Equal(new LocalDate(2026, 7, 29), sprint.StartDate);
        Assert.Equal("2026_S16_Jul29-Aug11", sprint.SprintId);
    }
}
