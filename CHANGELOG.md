# Changelog

## [0.2.1](https://github.com/flashpi1/flashpi-dotnet/compare/v0.2.0...v0.2.1) (2026-11-25)


### Bug fixes

* **models:** sync the API contract of 2026-11-24 ([0946e1f](https://github.com/flashpi1/flashpi-dotnet/commit/0946e1ff5a76a8a9e22947917b1bd36d1750dcb5))

## [0.2.0](https://github.com/flashpi1/flashpi-dotnet/compare/v0.1.0...v0.2.0) (2026-11-18)


### Features

* **models:** let the last payment leave out its amount ([8c5c2ec](https://github.com/flashpi1/flashpi-dotnet/commit/8c5c2ec0813e530ecb6159b10ca4ee6581e0de16))

## 0.1.0 (2026-11-14)


### Features

* add the client and every operation of the API ([9f68159](https://github.com/flashpi1/flashpi-dotnet/commit/9f68159247ed41c3e22a111741e833c69b3e6f63))
* add the exceptions and the checks of keys and headers ([877ac1e](https://github.com/flashpi1/flashpi-dotnet/commit/877ac1ea0464d8b3718cf90ae465e2be6436f6b9))
* generate the models from the OpenAPI contract ([4df3090](https://github.com/flashpi1/flashpi-dotnet/commit/4df309075af231ec1fb1dd76984aef74146337e2))
* read the ten whole-number counters as int ([ce45448](https://github.com/flashpi1/flashpi-dotnet/commit/ce45448b6f60f9590e1e30ebc7b4cb669577a2db))
* verify the webhooks Flashpi sends ([480590c](https://github.com/flashpi1/flashpi-dotnet/commit/480590c4f45cc727700ac067e06d13d6bded5cfe))


### Bug fixes

* convert a null text to the empty enum value instead of throwing ([9d725d1](https://github.com/flashpi1/flashpi-dotnet/commit/9d725d179e4231f99ac91dfc628641f2a375561c))
* harden how an attempt fails and the request its answer keeps ([5f7b9b9](https://github.com/flashpi1/flashpi-dotnet/commit/5f7b9b968ac295f25a01106c093b72a3b323aa2e))
* harden the generator and hide the webhook signing secret ([d496cdd](https://github.com/flashpi1/flashpi-dotnet/commit/d496cdde9011c4aec5ae57a169ad3efffd10b795))
* keep the documentId of odd problem details and read only real dates ([6fbf1e5](https://github.com/flashpi1/flashpi-dotnet/commit/6fbf1e529aa9aace794b95b2aca8c4a512686056))
* lift .NET Framework's limit of 2 connections per server ([8b376c6](https://github.com/flashpi1/flashpi-dotnet/commit/8b376c6b6bdd9e525572d748dc9f943028b81ff3))
* read a null number, boolean or date as its default ([4988fcf](https://github.com/flashpi1/flashpi-dotnet/commit/4988fcf3ccf70a16460f26018ba28788ea08da81))
