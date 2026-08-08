using Moq;
using MftSearchWpf.Services;
using Xunit;

namespace Tachyon.Tests
{
    public class MftEngineTests
    {
        [Fact]
        public void IsAdministrator_NonWindowsOS_ReturnsFalse()
        {
            // Arrange
            var mockIdentity = new Mock<ISystemIdentity>();
            mockIdentity.Setup(x => x.IsWindowsOS()).Returns(false);

            // Note: IsAdministratorRole should not even be called
            mockIdentity.Setup(x => x.IsAdministratorRole()).Returns(true);

            // Act
            bool result = MftEngine.IsAdministrator(mockIdentity.Object);

            // Assert
            Assert.False(result);
            mockIdentity.Verify(x => x.IsAdministratorRole(), Times.Never);
        }

        [Fact]
        public void IsAdministrator_WindowsOS_NonAdminUser_ReturnsFalse()
        {
            // Arrange
            var mockIdentity = new Mock<ISystemIdentity>();
            mockIdentity.Setup(x => x.IsWindowsOS()).Returns(true);
            mockIdentity.Setup(x => x.IsAdministratorRole()).Returns(false);

            // Act
            bool result = MftEngine.IsAdministrator(mockIdentity.Object);

            // Assert
            Assert.False(result);
            mockIdentity.Verify(x => x.IsWindowsOS(), Times.Once);
            mockIdentity.Verify(x => x.IsAdministratorRole(), Times.Once);
        }

        [Fact]
        public void IsAdministrator_WindowsOS_AdminUser_ReturnsTrue()
        {
            // Arrange
            var mockIdentity = new Mock<ISystemIdentity>();
            mockIdentity.Setup(x => x.IsWindowsOS()).Returns(true);
            mockIdentity.Setup(x => x.IsAdministratorRole()).Returns(true);

            // Act
            bool result = MftEngine.IsAdministrator(mockIdentity.Object);

            // Assert
            Assert.True(result);
            mockIdentity.Verify(x => x.IsWindowsOS(), Times.Once);
            mockIdentity.Verify(x => x.IsAdministratorRole(), Times.Once);
        }
    }
}
