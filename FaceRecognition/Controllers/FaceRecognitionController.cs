using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;


//using Amazon.S3;
//using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace FaceRecognition.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FaceRecognitionController : ControllerBase
    {
        private readonly ILogger<FaceRecognitionController> _logger;
        string accessId = "AKIAXZZKY5LKZ6YC2P2A";
        string secretKey = "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";

        public FaceRecognitionController(ILogger<FaceRecognitionController> logger)
        {
            _logger = logger;
        }

        [HttpGet("compare")]
        public async Task<IActionResult> CompareTwoFaces()
        {
            string sourceImageUrl = "https://res.cloudinary.com/affable-digital-services/image/upload/v1693179083/ubvfn5zwhjdx3cz86jjy.jpg";
            string targetImageUrl = "https://res.cloudinary.com/affable-digital-services/image/upload/v1693179083/ubvfn5zwhjdx3cz86jjy.jpg"; // Replace with your target image URL

            try
            {
                float similarityThreshold = 70F;
                var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, Amazon.RegionEndpoint.USWest2);
                Image imageSource = new();
                Image imageTarget = new();

                using (HttpClient client = new())
                {
                    byte[] sourceImageData = await client.GetByteArrayAsync(sourceImageUrl);
                    imageSource.Bytes = new MemoryStream(sourceImageData);

                    byte[] targetImageData = await client.GetByteArrayAsync(targetImageUrl);
                    imageTarget.Bytes = new MemoryStream(targetImageData);
                }

                var compareFacesRequest = new CompareFacesRequest
                {
                    SourceImage = imageSource,
                    TargetImage = imageTarget,
                    SimilarityThreshold = similarityThreshold,
                };

                var compareFacesResponse = await rekognitionClient.CompareFacesAsync(compareFacesRequest);

                foreach (var match in compareFacesResponse.FaceMatches)
                {
                    ComparedFace face = match.Face;
                    BoundingBox position = face.BoundingBox;
                    _logger.LogInformation($"Face at {position.Left} {position.Top} matches with {match.Similarity}% confidence.");
                }

                _logger.LogInformation($"Found {compareFacesResponse.UnmatchedFaces.Count} face(s) that did not match.");

                return Ok("Comparison completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during face comparison: {ex.Message}");
                return StatusCode(500, "An error occurred during face comparison.");
            }
        }

        // create collection
        [HttpPost("create-collection")]
        public async Task<IActionResult> CreateCollection(string collectionId)
        {
            var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, Amazon.RegionEndpoint.USWest2);
            _logger.LogInformation($"Creating collection: {collectionId}");

            CreateCollectionRequest createCollectionRequest = new()
            {
                CollectionId = collectionId
            };
            CreateCollectionResponse createCollectionResponse = await rekognitionClient.CreateCollectionAsync(createCollectionRequest);
            _logger.LogInformation($"CollectionArn : {createCollectionResponse.CollectionArn}");
            _logger.LogInformation($"Status code : {createCollectionResponse.StatusCode}");
            return Ok("Collection created successfully");
        }

        // describe a collection
        [HttpGet("decsribe-collection")]
        public async Task<IActionResult> DescribeCollection(string collectionId)
        {
            var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, Amazon.RegionEndpoint.USWest2);
            _logger.LogInformation($"Describing collection: {collectionId}");
            try
            {
                DescribeCollectionRequest describeCollectionRequest = new()
                {
                    CollectionId = collectionId
                };

                var describeCollectionResponse = await rekognitionClient.DescribeCollectionAsync(describeCollectionRequest);
                _logger.LogInformation($"Collection ARN: {describeCollectionResponse.CollectionARN}");
                _logger.LogInformation($"Face count: {describeCollectionResponse.FaceCount}");
                _logger.LogInformation($"Face model version: {describeCollectionResponse.FaceModelVersion}");
                _logger.LogInformation($"Created: {describeCollectionResponse.CreationTimestamp}");
                return Ok("Describing collection successful");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error describing collection: {ex.Message}");
                return StatusCode(500, "An error occurred in describing collection.");
            }
        }

        [HttpPost("create-s3bucket")]
        public async Task<IActionResult> CreateBucket()
        {
            // Create an Amazon S3 client
            var awsCredentials = new BasicAWSCredentials(accessId, secretKey);
            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USWest2
            };
            var s3Client = new AmazonS3Client(awsCredentials, config);
            string bucketName = "test-collection-bucket";
            try
            {
                await s3Client.PutBucketAsync(new PutBucketRequest {
                    BucketName = bucketName, UseClientRegion = true
                });
                _logger.LogInformation($"S3 bucket '{bucketName}' created successfully.");
                return Ok("Bucket created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating s3 bucket: {ex.Message}");
                return StatusCode(500, "s3 bucket creation failed.");
            }
        }

        // add face to collection
        [HttpPost("add-single-face-to-collection")]
        public async Task<IActionResult> AddSingleFaceToCollection(string bucketName, string collectionId, string imageName)
        {
            string imageUrl = GetPresignUrl(bucketName, imageName);
            Console.WriteLine("Image url = " + imageUrl);
            Image imageSource = new();

            using (HttpClient client = new())
            {
                byte[] sourceImageData = await client.GetByteArrayAsync(imageUrl);
                imageSource.Bytes = new MemoryStream(sourceImageData);
            }
            var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, RegionEndpoint.USWest2);
            Image image = new()
            {
                S3Object = new Amazon.Rekognition.Model.S3Object
                {
                    Bucket = bucketName,
                    Name = imageName
                },
            };

            IndexFacesRequest indexFacesRequest = new()
            {
                Image = image,
                CollectionId = collectionId,
                ExternalImageId = imageSource.ToString(),
                DetectionAttributes = new List<string>() { "ALL" },
            };

            try
            {
                IndexFacesResponse indexFacesResponse = await rekognitionClient.IndexFacesAsync(indexFacesRequest);
                _logger.LogInformation("Image added");
                foreach (FaceRecord faceRecord in indexFacesResponse.FaceRecords)
                {
                    _logger.LogInformation($"Face detected: Faceid is {faceRecord.Face.FaceId}");
                }
                return Ok("Image added to collection");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating s3 bucket: {ex.Message}");
                return StatusCode(500, "s3 bucket creation failed.");
            }
        }

        [HttpPost("add-all-faces-to-collection")]
        public async Task<IActionResult> AddBucketFacesToCollection(string bucketName, string collectionId)
        {
            var imageNames = await ListFilesInBucket(bucketName);
            try
            {
                foreach (var imageName in imageNames)
                {
                    await AddSingleFaceToCollection(bucketName, collectionId, imageName);
                    _logger.LogInformation($"image '{imageName}' added to collection successfully");
                }
                return Ok("All images added successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding images to collection: {ex.Message}");
                return StatusCode(500, "failed to add images to collection.");
            }
        }


        [HttpGet("search-a-face")]
        public async Task<IActionResult> SearchFacesMatchingImage(string collectionId, string bucketName, string imageName)
        {
            string imageUrl = GetPresignUrl(bucketName, imageName);
            Console.WriteLine("Image url = " + imageUrl);
            Image imageSource = new();

            using (HttpClient client = new())
            {
                byte[] sourceImageData = await client.GetByteArrayAsync(imageUrl);
                imageSource.Bytes = new MemoryStream(sourceImageData);
            }
            var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, RegionEndpoint.USWest2);
            Image image = new()
            {
                S3Object = new Amazon.Rekognition.Model.S3Object
                {
                    Bucket = bucketName,
                    Name = imageName
                },
            };

            var searchFacesByImageRequest = new SearchFacesByImageRequest
            {
                CollectionId = collectionId,
                Image = image,
                FaceMatchThreshold = 70F,
                MaxFaces = 2
            };

            try
            {
                SearchFacesByImageResponse searchFacesByImageResponse = await rekognitionClient.SearchFacesByImageAsync(searchFacesByImageRequest);
                _logger.LogInformation("Faces matching largest face in image from " + imageName);
                searchFacesByImageResponse.FaceMatches.ForEach(face => 
                {
                    _logger.LogInformation($"FaceId: {face.Face.FaceId}, Similarity: {face.Similarity}");
                });
                return Ok("Face matches found");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error searching for face: {ex.Message}");
                return StatusCode(500, "face searching failed.");
            }
        }

        [HttpGet("list-faces-in-a-collection")]
        public async Task<IActionResult> ListFacesInCollection(string collectionId)
        {
            var rekognitionClient = new AmazonRekognitionClient(accessId, secretKey, RegionEndpoint.USWest2);
            var listFacesResponse = new ListFacesResponse();
            _logger.LogInformation($"Faces in collection {collectionId}");

            var listFacesRequest = new ListFacesRequest
            {
                CollectionId = collectionId,
                MaxResults = 2
            };

            do
            {
                listFacesResponse = await rekognitionClient.ListFacesAsync(listFacesRequest);
                listFacesResponse.Faces.ForEach(face => 
                {
                    _logger.LogInformation(face.FaceId);
                });

                listFacesRequest.NextToken = listFacesResponse.NextToken;
            }
            while (!string.IsNullOrEmpty(listFacesResponse.NextToken));
            return Ok("Faces Retrieved Successfully");
        }

        // ==================== Private Methods ======================

        private string GetPresignUrl(string bucketName, string imageName)
        {
            var s3Client = new AmazonS3Client(accessId, secretKey, RegionEndpoint.USWest2);
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = imageName,
                Expires = DateTime.UtcNow.AddHours(5)
            };
            string url = s3Client.GetPreSignedURL(request);
            return url;
        }
        
        private async Task<List<string>> ListFilesInBucket(string bucketName)
        {
            var fileList = new List<string>();
            var s3Client = new AmazonS3Client(accessId, secretKey, RegionEndpoint.USWest2);
            var request = new ListObjectsV2Request
            {
                BucketName = bucketName
            };
            ListObjectsV2Response response;
            do
            {
                response = await s3Client.ListObjectsV2Async(request);
                foreach(Amazon.S3.Model.S3Object entry in response.S3Objects)
                {
                    fileList.Add(entry.Key);
                }
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated);
            return fileList;
        }
    }
}