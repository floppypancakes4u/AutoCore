namespace AutoCore.Game.Constants;

public enum MailOpcode
{
    MailCreateRequest           = 0x0,
    MailCreateResponse          = 0x1,
    MailNotification            = 0x2,
    MailListRequest             = 0x3,
    MailListResponse            = 0x4,
    MailContentCollectRequest   = 0x5,
    MailContentCollectResponse  = 0x6,
    MailDeleteRequest           = 0x7,
    MailDeleteResponse          = 0x8,
    AuctionCreateRequest        = 0x9,
    AuctionCreateResponse       = 0xA,
    AuctionCancelRequest        = 0xB,
    AuctionCancelResponse       = 0xC,
    AuctionListRequest          = 0xD,
    AuctionListResponse         = 0xE,
    AuctionBidRequest           = 0xF,
    AuctionBidResponse          = 0x10
}
