const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const collectionId = crypto.randomUUID();

const collection = {
  info: {
    name: "New Collection",
    schema: "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    _postman_id: collectionId
  },
  item: [
    {
      name: "BandVault API",
      request: {
        method: "POST",
        header: [],
        body: {
          mode: "raw",
          raw: JSON.stringify({
            title: "SEVERED HOPES SHATTERED INNOCENCE Tote Bag",
            description: "SEVERED HOPES SHATTERED INNOCENCE Tote Bag",
            price: 20.00,
            stockQuantity: 50,
            imageUrl: "https://www.pinterest.com/pin/711639178674429249/",
            categoryId: 2
          }, null, 2),
          options: {
            raw: {
              language: "json"
            }
          }
        },
        url: {
          raw: "https://localhost:7219/api/merch",
          protocol: "https",
          host: ["localhost"],
          port: "7219",
          path: ["api", "merch"]
        }
      },
      event: [
        {
          listen: "test",
          script: {
            type: "text/javascript",
            exec: [
              "pm.test(\"Status code is 201 Created\", function () { pm.response.to.have.status(201); });"
            ]
          }
        }
      ],
      response: []
    },
    {
      name: "Get Merch List",
      request: {
        method: "GET",
        header: [],
        url: {
          raw: "https://localhost:7219/api/merch",
          protocol: "https",
          host: ["localhost"],
          port: "7219",
          path: ["api", "merch"]
        }
      },
      event: [
        {
          listen: "test",
          script: {
            type: "text/javascript",
            exec: [
              "pm.test(\"Status code is 200 OK\", function () { pm.response.to.have.status(200); });"
            ]
          }
        }
      ],
      response: []
    },
    {
      name: "Get Merch List (Paginated)",
      request: {
        method: "GET",
        header: [],
        url: {
          raw: "https://localhost:7219/api/merch",
          protocol: "https",
          host: ["localhost"],
          port: "7219",
          path: ["api", "merch"]
        }
      },
      event: [
        {
          listen: "test",
          script: {
            type: "text/javascript",
            exec: [
              "pm.test(\"Status code is 200 OK\", function () { pm.response.to.have.status(200); });",
              "pm.test(\"Response contains pagination fields\", function () { var jsonData = pm.response.json(); pm.expect(jsonData).to.have.property('totalItems'); pm.expect(jsonData).to.have.property('totalPages'); pm.expect(jsonData).to.have.property('currentPage'); pm.expect(jsonData).to.have.property('pageSize'); pm.expect(jsonData).to.have.property('data'); });",
              "pm.test(\"Data is an array\", function () { var jsonData = pm.response.json(); pm.expect(jsonData.data).to.be.an('array'); });"
            ]
          }
        }
      ],
      response: []
    },
    {
      name: "Create Merch(error)",
      request: {
        method: "POST",
        header: [],
        body: {
          mode: "raw",
     
          raw: JSON.stringify({
            title: "",
            description: "",
            price: -5,
            categoryId: 1
          }, null, 2),
          options: {
            raw: {
              language: "json"
            }
          }
        },
        url: {
          raw: "https://localhost:7219/api/merch",
          protocol: "https",
          host: ["localhost"],
          port: "7219",
          path: ["api", "merch"]
        }
      },
      event: [
        {
          listen: "test",
          script: {
            type: "text/javascript",
            exec: [
              "// Перевірка, що API не дає створити пустий/неправильний об'єкт",
              "pm.test(\"Status code is 400 Bad Request\", function () { pm.response.to.have.status(400); });",
              "pm.test(\"Response has validation errors\", function () { var jsonData = pm.response.json(); pm.expect(jsonData).to.have.property('errors'); });"
            ]
          }
        }
      ],
      response: []
    },
    {
      name: "Creare",
      request: {
        method: "POST",
        header: [],
        body: {
          mode: "raw",
          raw: JSON.stringify({
            title: "Band T-Shirt",
            description: "Black t-shirt with logo",
            price: 25.00,
            stockQuantity: 100,
            categoryId: 1
          }, null, 2),
          options: {
            raw: {
              language: "json"
            }
          }
        },
        url: {
          raw: "https://localhost:7219/api/merch",
          protocol: "https",
          host: ["localhost"],
          port: "7219",
          path: ["api", "merch"]
        }
      },
      event: [
        {
          listen: "test",
          script: {
            type: "text/javascript",
            exec: [
              "pm.test(\"Status code is 201 Created\", function () { pm.response.to.have.status(201); });"
            ]
          }
        }
      ],
      response: []
    }
  ]
};

const outputPath = path.join(__dirname, 'New Collection.postman_collection.json');
fs.writeFileSync(outputPath, JSON.stringify(collection, null, 2), 'utf8');
console.log('Collection written to:', outputPath);

// Verify it's valid JSON
const raw = fs.readFileSync(outputPath, 'utf8');
JSON.parse(raw);
console.log('JSON is valid.');