# 📄 Document Processing System

A streamlined document processing application built with .NET 8 Blazor Server that leverages AWS Bedrock AI for intelligent document analysis and summarization.

![Application Dashboard](screenshots/dashboard.png)

## 🌟 Key Features

- **🤖 AI-Powered Processing**: Integration with AWS Bedrock (Claude 3.5 Sonnet) for intelligent document summarization
- **📁 Multi-Format Support**: Process PDF documents with text extraction
- **📤 Easy Upload**: Drag-and-drop interface for document uploads
- **☁️ Flexible Storage**: Support for both AWS S3 and local file storage
- **🔐 Secure Credentials**: AWS Secrets Manager integration for database connection strings
- **💾 Database Support**: Works with both SQL Server and PostgreSQL
- **📊 Document Management**: Track upload status, view summaries, and manage documents
- **🔄 Status Tracking**: Real-time processing status (Pending, Processing, Processed, Failed)

## 🏗️ Architecture

Simple single-project Blazor Server architecture:

```
DPSX/
└── src/
    └── DocumentProcessor.Web/
        ├── Components/        # Blazor components and pages
        ├── Data/             # DbContext and database configuration
        ├── Models/           # Document entity and enums
        ├── Services/         # Business logic (AI, Storage, Processing)
        └── wwwroot/          # Static files (CSS, images)
```

## 🚀 Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- SQL Server or PostgreSQL database
- AWS Account with:
  - Bedrock access (Claude 3.5 Sonnet model)
  - S3 bucket (optional, for cloud storage)
  - Secrets Manager (for database credentials)
- AWS CLI configured with appropriate credentials

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/AES-Git/AESDPS.git
   cd AESDPS
   ```

2. **Configure AWS Credentials**

   Ensure your AWS credentials are configured:
   ```bash
   aws configure
   ```

   Or set environment variables:
   ```bash
   export AWS_ACCESS_KEY_ID=your_access_key
   export AWS_SECRET_ACCESS_KEY=your_secret_key
   export AWS_DEFAULT_REGION=us-east-1
   ```

3. **Set Up Database Credentials in AWS Secrets Manager**

   The application retrieves database credentials from AWS Secrets Manager. Create secrets with the following structure:

   **For PostgreSQL** (secret name: `atx-db-modernization-atx-db-modernization-1-target`):
   ```json
   {
     "username": "your_username",
     "password": "your_password",
     "host": "your-db-host.rds.amazonaws.com",
     "port": "5432"
   }
   ```

   **For SQL Server** (secret with description: `Password for RDS MSSQL used for MAM319.`):
   ```json
   {
     "username": "your_username",
     "password": "your_password",
     "host": "your-db-host.rds.amazonaws.com",
     "port": "1433",
     "dbname": "your_database_name"
   }
   ```

4. **Configure Application Settings** (Optional)

   Update `src/DocumentProcessor.Web/appsettings.json` for local development fallback:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=DocumentProcessor;Integrated Security=true;TrustServerCertificate=True;"
     },
     "Logging": {
       "LogLevel": {
         "Default": "Information",
         "Microsoft.AspNetCore": "Warning"
       }
     }
   }
   ```

5. **Run the application**
   ```bash
   cd src/DocumentProcessor.Web
   dotnet run
   ```

6. **Access the application**

   Navigate to `http://localhost:5197`

## 📋 Features Overview

### Document Upload & Processing

![Document Upload](screenshots/upload.png)

- **Drag-and-drop Interface**: Easy file upload with visual feedback
- **PDF Text Extraction**: Automatic text extraction using PdfPig
- **AI Summarization**: Generate intelligent summaries using AWS Bedrock
- **Document Classification**: Categorize documents automatically
- **Status Tracking**: Monitor document processing status in real-time

### Storage Options

The application supports two storage backends:

1. **Local File System**: Documents stored in `uploads/` directory
2. **AWS S3**: Cloud storage with automatic bucket management

Storage is configured automatically based on AWS credentials availability.

### Database Flexibility

- **SQL Server**: Primary database support with Entity Framework Core
- **PostgreSQL**: Alternative database option for cloud deployments
- **Automatic Migration**: Database schema created automatically on first run

## 🛠️ Technology Stack

- **Backend**:
  - .NET 8 with C# 12
  - ASP.NET Core Blazor Server
  - Entity Framework Core 8

- **Frontend**:
  - Blazor Server-Side Rendering
  - Bootstrap 5 for responsive UI
  - Custom CSS for styling

- **Database**:
  - Microsoft SQL Server (EntityFrameworkCore.SqlServer 8.0.10)
  - PostgreSQL support (via configuration)

- **Cloud Services**:
  - AWS Bedrock (Claude 3.5 Sonnet for AI processing)
  - AWS S3 (Document storage)
  - AWS Secrets Manager (Credential management)

- **Document Processing**:
  - PdfPig 0.1.11 (PDF text extraction)
  - CsvHelper 33.1.0 (CSV processing)

## 📁 Project Structure

```
src/DocumentProcessor.Web/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor       # Main app layout
│   │   └── NavMenu.razor          # Navigation menu
│   └── Pages/
│       └── Home.razor             # Main page with upload and document list
├── Data/
│   └── AppDbContext.cs            # Entity Framework DbContext
├── Models/
│   └── Document.cs                # Document entity with status enum
├── Services/
│   ├── AIService.cs               # AWS Bedrock integration
│   ├── DatabaseInfoService.cs    # Database metadata
│   ├── DocumentProcessingService.cs  # Document processing logic
│   ├── FileStorageService.cs     # S3/local file storage
│   └── SecretsService.cs         # AWS Secrets Manager
├── wwwroot/
│   └── css/
│       └── app.css                # Custom styles
└── Program.cs                     # App configuration and startup
```

## 🔧 Configuration

### AWS Bedrock Model

The application uses Claude 3.5 Sonnet v2 by default:
- Model ID: `anthropic.claude-3-5-sonnet-20241022-v2:0`
- Region: Configured via AWS CLI or environment variables
- Max Tokens: 1024 for summaries

### File Storage

**Local Storage** (default fallback):
```
DocumentProcessor.Web/uploads/
```

**AWS S3 Storage**:
- Bucket: `document-processor-uploads-{accountId}`
- Auto-created if it doesn't exist
- Files organized by document ID

### Database Connection

The app attempts to connect in this order:
1. AWS Secrets Manager (PostgreSQL target secret)
2. AWS Secrets Manager (SQL Server with "MAM319" description)
3. Local connection string from appsettings.json

## 🔒 Security Features

- **AWS Secrets Manager**: Database credentials never stored in code
- **Secure File Storage**: Documents stored with unique GUIDs
- **Input Validation**: File type and size validation
- **SQL Injection Prevention**: Parameterized queries via EF Core
- **XSS Protection**: Built-in Blazor security features
- **Soft Deletes**: Documents marked as deleted, not physically removed

## 📊 Document Status States

Documents progress through the following states:

1. **Pending**: Uploaded, waiting for processing
2. **Processing**: Currently being analyzed by AI
3. **Processed**: Successfully processed with summary available
4. **Failed**: Processing encountered an error

## 🚢 Deployment

### AWS Deployment

The application is designed for AWS deployment:

1. **Database**: RDS (SQL Server or PostgreSQL)
2. **Storage**: S3 for document files
3. **Compute**: Elastic Beanstalk, ECS, or EC2
4. **Credentials**: Secrets Manager for sensitive data

### Docker (Future)

Docker support can be added with a standard .NET 8 Dockerfile.

## 📸 Screenshots

### Home/Upload Page
![Main interface showing document upload and list](screenshots/home.png)

### Document Processing
![Document being processed with status updates](screenshots/processing.png)

### Document Summary View
![AI-generated document summary display](screenshots/summary.png)

## 🆘 Troubleshooting

### Database Connection Issues

If you see database connection errors:
1. Verify AWS Secrets Manager secrets are configured correctly
2. Check AWS credentials have permissions to access Secrets Manager
3. Fallback to local connection string in appsettings.json

### AWS Bedrock Access

If AI processing fails:
1. Verify AWS region supports Bedrock
2. Check IAM permissions include Bedrock access
3. Ensure Claude model access is enabled in AWS console

### File Upload Issues

If uploads fail:
1. Check file size and format (PDF supported)
2. Verify local uploads directory exists and is writable
3. For S3: confirm S3 bucket permissions and AWS credentials

## 🗺️ Roadmap

### Planned Features
- Support for additional document formats (DOCX, TXT, images)
- Batch document processing
- Document search and filtering
- Export capabilities
- User authentication
- Document versioning
- Advanced AI analysis options

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License.

## 🙏 Acknowledgments

- Built with [.NET 8](https://dotnet.microsoft.com/)
- AI powered by [AWS Bedrock](https://aws.amazon.com/bedrock/)
- UI framework by [Bootstrap](https://getbootstrap.com/)
- PDF processing by [PdfPig](https://github.com/UglyToad/PdfPig)

---

**Built with ❤️ using .NET 8 and AWS Bedrock AI**
