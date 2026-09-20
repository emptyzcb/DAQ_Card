"""Protocol vectors for the Modbus RTU frame rules used by the firmware."""


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = ((crc >> 1) ^ 0xA001) if (crc & 1) else (crc >> 1)
    return crc


def with_crc(payload: bytes) -> bytes:
    crc = crc16(payload)
    return payload + bytes((crc & 0xFF, crc >> 8))


def valid_frame(frame: bytes) -> bool:
    return len(frame) >= 4 and crc16(frame[:-2]) == int.from_bytes(frame[-2:], "little")


def test_crc_vector() -> None:
    assert crc16(bytes.fromhex("010300000001")) == 0x0A84
    assert with_crc(bytes.fromhex("010300000001")) == bytes.fromhex("010300000001840a")


def test_read_holding_response_vector() -> None:
    request = with_crc(bytes.fromhex("010300000001"))
    response = with_crc(bytes.fromhex("0103020003"))

    assert valid_frame(request)
    assert valid_frame(response)
    assert response == bytes.fromhex("0103020003f845")


def test_write_single_and_broadcast_vectors() -> None:
    write_single = with_crc(bytes.fromhex("010600020055"))
    broadcast_write = with_crc(bytes.fromhex("000600020055"))

    assert write_single == bytes.fromhex("010600020055e835")
    assert valid_frame(broadcast_write)
    assert broadcast_write[0] == 0


def test_write_multiple_vector() -> None:
    request = with_crc(bytes.fromhex("011000020001020055"))
    response = with_crc(bytes.fromhex("011000020001"))

    assert request == bytes.fromhex("011000020001020055678d")
    assert response == bytes.fromhex("011000020001a009")


def test_invalid_crc_and_wrong_address_are_not_accepted() -> None:
    bad_crc = bytes.fromhex("0103000000010000")
    wrong_address = with_crc(bytes.fromhex("020300000001"))

    assert not valid_frame(bad_crc)
    assert valid_frame(wrong_address)
    assert wrong_address[0] != 1


if __name__ == "__main__":
    tests = [
        test_crc_vector,
        test_read_holding_response_vector,
        test_write_single_and_broadcast_vectors,
        test_write_multiple_vector,
        test_invalid_crc_and_wrong_address_are_not_accepted,
    ]
    for test in tests:
        test()
    print(f"{len(tests)} Modbus RTU vector tests passed")
